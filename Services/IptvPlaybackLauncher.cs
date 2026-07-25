namespace TinyCinema;

public static class IptvPlaybackLauncher
{
    public static void Play(IptvChannel channel) =>
        ExternalPlayerLauncher.Launch(PlayerNames.FFPLAY, channel.StreamUrl);
}
