namespace BepInEx.Logging
{
    public static class Logger
    {
        public static ManualLogSource CreateLogSource(string sourceName) =>
            new ManualLogSource(sourceName);
    }
}