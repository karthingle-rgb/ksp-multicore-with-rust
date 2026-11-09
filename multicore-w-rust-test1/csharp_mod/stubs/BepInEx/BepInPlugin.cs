using System;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class BepInPlugin : Attribute
    {
        public string Guid { get; }
        public string Name { get; }
        public string Version { get; }

        public BepInPlugin(string guid, string name, string version)
        {
            Guid = guid;
            Name = name;
            Version = version;
        }
    }
}