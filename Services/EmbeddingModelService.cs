using System.Diagnostics;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Serilog;

namespace TinyCinema;

public sealed class EmbeddingModelService : IDisposable
{
    public const string ModelName = "intfloat/e5-small-v2";
    public const int VectorDimension = SearchIndexData.VectorDimension;
    private const int MaxSequenceLength = 512;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string _inputIdsName;
    private readonly string _attentionMaskName;
    private readonly string? _tokenTypeIdsName;
    private readonly string _outputName;
    private readonly object _inferenceLock = new();

    private EmbeddingModelService(
        InferenceSession session,
        BertTokenizer tokenizer,
        string inputIdsName,
        string attentionMaskName,
        string? tokenTypeIdsName,
        string outputName)
    {
        _session = session;
        _tokenizer = tokenizer;
        _inputIdsName = inputIdsName;
        _attentionMaskName = attentionMaskName;
        _tokenTypeIdsName = tokenTypeIdsName;
        _outputName = outputName;
    }

    public static EmbeddingModelService Create(SearchIndexBuildReporter? reporter = null)
    {
        if (!EmbeddingModelPaths.IsModelAvailable())
            throw new FileNotFoundException("Embedding model files were not found.", EmbeddingModelPaths.GetModelPath());

        var modelPath = EmbeddingModelPaths.GetModelPath();
        var vocabPath = EmbeddingModelPaths.GetVocabPath();

        reporter?.Log($"Model ONNX path: {modelPath}");
        reporter?.Log($"Model ONNX size: {FormatFileSize(modelPath)}");
        reporter?.Log($"Vocab path: {vocabPath}");
        reporter?.Log($"Vocab size: {FormatFileSize(vocabPath)}");

        reporter?.Log("Creating ONNX InferenceSession (this often takes 30-120 seconds on first load)...");
        var sessionStopwatch = Stopwatch.StartNew();

        using var sessionOptions = new SessionOptions();
        sessionOptions.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        InferenceSession session;
        try
        {
            session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load ONNX model from '{modelPath}'. {ex.Message}", ex);
        }
        reporter?.Log($"ONNX InferenceSession ready in {sessionStopwatch.Elapsed.TotalSeconds:F1}s.");

        reporter?.Log("ONNX inputs: " + string.Join(", ", session.InputMetadata.Keys));
        reporter?.Log("ONNX outputs: " + string.Join(", ", session.OutputMetadata.Keys));

        var inputIdsName = PickInputName(session, "input_ids");
        var attentionMaskName = PickInputName(session, "attention_mask");
        var tokenTypeIdsName = TryPickInputName(session, "token_type_ids");
        var outputName = PickOutputName(session);

        reporter?.Log($"Using input_ids={inputIdsName}, attention_mask={attentionMaskName}, output={outputName}");

        reporter?.Log("Loading BERT tokenizer from vocab.txt...");
        var tokenizerStopwatch = Stopwatch.StartNew();
        var tokenizer = BertTokenizer.Create(
            vocabPath,
            new BertOptions { LowerCaseBeforeTokenization = true });
        reporter?.Log($"Tokenizer ready in {tokenizerStopwatch.ElapsedMilliseconds}ms.");

        reporter?.Log("Running warm-up embedding to verify model output...");
        var service = new EmbeddingModelService(
            session,
            tokenizer,
            inputIdsName,
            attentionMaskName,
            tokenTypeIdsName,
            outputName);

        var warmupStopwatch = Stopwatch.StartNew();
        var warmup = service.EmbedText("query: warm up");
        reporter?.Log(
            $"Warm-up embedding OK - {warmup.Length} dimensions in {warmupStopwatch.ElapsedMilliseconds}ms.");

        Log.Information(
            "Embedding model loaded. Inputs: {InputIds}, {AttentionMask}, {TokenTypeIds}. Output: {Output}",
            inputIdsName,
            attentionMaskName,
            tokenTypeIdsName ?? "(none)",
            outputName);

        return service;
    }

    public float[] EmbedQuery(string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return Array.Empty<float>();

        return EmbedText(SearchPassageBuilder.BuildQuery(userQuery));
    }

    public float[] EmbedPassage(string passage) => EmbedText(passage);

    public float[] EmbedPassage(MovieCatalogRecord record) =>
        EmbedText(SearchPassageBuilder.BuildPassage(record));

    private float[] EmbedText(string text)
    {
        lock (_inferenceLock)
        {
            var ids = _tokenizer.EncodeToIds(text, addSpecialTokens: true, considerPreTokenization: true);
            if (ids.Count > MaxSequenceLength)
                ids = ids.Take(MaxSequenceLength).ToArray();

            var sequenceLength = ids.Count;
            if (sequenceLength <= 0)
                return new float[VectorDimension];

            var inputIdsTensor = new DenseTensor<long>(new[] { 1, sequenceLength });
            var attentionMaskTensor = new DenseTensor<long>(new[] { 1, sequenceLength });
            var tokenTypeIdsTensor = new DenseTensor<long>(new[] { 1, sequenceLength });
            var attentionMask = new long[sequenceLength];

            for (var i = 0; i < sequenceLength; i++)
            {
                inputIdsTensor[0, i] = ids[i];
                attentionMaskTensor[0, i] = 1;
                tokenTypeIdsTensor[0, i] = 0;
                attentionMask[i] = 1;
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputIdsName, inputIdsTensor),
                NamedOnnxValue.CreateFromTensor(_attentionMaskName, attentionMaskTensor)
            };

            if (_tokenTypeIdsName != null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, tokenTypeIdsTensor));

            using var results = _session.Run(inputs);
            var output = results.First(result =>
                    result.Name.Equals(_outputName, StringComparison.OrdinalIgnoreCase))
                .AsEnumerable<float>()
                .ToArray();

            return MeanPoolAndNormalize(output, attentionMask, sequenceLength);
        }
    }

    private static float[] MeanPoolAndNormalize(float[] output, long[] attentionMask, int sequenceLength)
    {
        var hiddenSize = VectorDimension;
        var pooled = new float[hiddenSize];
        var tokenCount = 0;

        if (output.Length == hiddenSize)
        {
            Array.Copy(output, pooled, hiddenSize);
            tokenCount = 1;
        }
        else if (output.Length == sequenceLength * hiddenSize)
        {
            for (var token = 0; token < sequenceLength; token++)
            {
                if (attentionMask[token] == 0)
                    continue;

                tokenCount++;
                var offset = token * hiddenSize;
                for (var dim = 0; dim < hiddenSize; dim++)
                    pooled[dim] += output[offset + dim];
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"Unexpected ONNX output length {output.Length} for sequence length {sequenceLength}. " +
                $"Expected {hiddenSize} or {sequenceLength * hiddenSize}.");
        }

        if (tokenCount > 1)
        {
            for (var dim = 0; dim < hiddenSize; dim++)
                pooled[dim] /= tokenCount;
        }

        NormalizeInPlace(pooled);
        return pooled;
    }

    private static void NormalizeInPlace(float[] vector)
    {
        var sumSquares = 0f;
        for (var i = 0; i < vector.Length; i++)
            sumSquares += vector[i] * vector[i];

        if (sumSquares <= float.Epsilon)
            return;

        var inv = 1f / MathF.Sqrt(sumSquares);
        for (var i = 0; i < vector.Length; i++)
            vector[i] *= inv;
    }

    private static string FormatFileSize(string path)
    {
        if (!File.Exists(path))
            return "missing";

        var bytes = new FileInfo(path).Length;
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} bytes"
        };
    }

    private static string PickInputName(InferenceSession session, string preferredName)
    {
        if (session.InputMetadata.ContainsKey(preferredName))
            return preferredName;

        return session.InputMetadata.Keys.First();
    }

    private static string? TryPickInputName(InferenceSession session, string preferredName) =>
        session.InputMetadata.ContainsKey(preferredName) ? preferredName : null;

    private static string PickOutputName(InferenceSession session)
    {
        foreach (var candidate in new[] { "sentence_embedding", "last_hidden_state", "output_0" })
        {
            if (session.OutputMetadata.ContainsKey(candidate))
                return candidate;
        }

        return session.OutputMetadata.Keys.First();
    }

    public void Dispose() => _session.Dispose();
}
