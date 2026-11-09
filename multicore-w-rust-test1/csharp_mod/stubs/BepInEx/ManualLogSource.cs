using System;

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        private readonly string _name;

        public ManualLogSource(string name)
        {
            _name = name;
        }

        public void LogInfo(object message) => Console.WriteLine($"[INFO  {_name}] {message}");
        public void LogWarning(object message) => Console.WriteLine($"[WARN  {_name}] {message}");
        public void LogError(object message) => Console.WriteLine($"[ERROR {_name}] {message}");
    }
}