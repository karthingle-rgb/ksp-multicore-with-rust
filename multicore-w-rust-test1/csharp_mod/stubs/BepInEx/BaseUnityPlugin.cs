using UnityEngine;
using BepInEx.Logging;

namespace BepInEx
{
    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        public ManualLogSource Logger { get; } = new ManualLogSource("BaseUnityPlugin");
    }
}