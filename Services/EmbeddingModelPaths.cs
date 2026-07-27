using System.IO;

namespace TinyCinema;

public static class EmbeddingModelPaths
{
    public const string ModelFolderName = "e5-small-v2";
    public const string ModelFileName = "model.onnx";
    public const string VocabFileName = "vocab.txt";
    public const string TokenizerFileName = "tokenizer.json";

    public static string GetModelDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "Assets", "Models", ModelFolderName);
    }

    public static string GetModelPath() => Path.Combine(GetModelDirectory(), ModelFileName);

    public static string GetVocabPath() => Path.Combine(GetModelDirectory(), VocabFileName);

    public static string GetTokenizerPath() => Path.Combine(GetModelDirectory(), TokenizerFileName);

    public static bool IsModelAvailable()
    {
        return File.Exists(GetModelPath()) &&
               (File.Exists(GetVocabPath()) || File.Exists(GetTokenizerPath()));
    }
}
