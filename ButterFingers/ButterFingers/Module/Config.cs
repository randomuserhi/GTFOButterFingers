using BepInEx;
using BepInEx.Configuration;

namespace ButterFingers.BepInEx {
    public static partial class ConfigManager {
        public static ConfigFile configFile;

        static ConfigManager() {
            string text = Path.Combine(Paths.ConfigPath, $"{Module.Name}.cfg");
            configFile = new ConfigFile(text, true);

            debug = configFile.Bind(
                "Debug",
                "enable",
                false,
                "Enables debug messages when true.");

            probability = configFile.Bind(
                "Settings",
                "probability",
                0.05f,
                "Probability that you fumble each roll.");

            distancePerRoll = configFile.Bind(
                "Settings",
                "distancePerRoll",
                1f,
                "Distance you need to travel before you may fumble.");

            force = configFile.Bind(
                "Settings",
                "force",
                200f,
                "The strength of your fumble.");

            makeNoise = configFile.Bind(
                "Settings",
                "makeNoise",
                true,
                "Should the item make noise that wakes up enemies as it bounces.");
        }

        public static bool Debug {
            get { return debug.Value; }
            set { debug.Value = value; }
        }
        private static ConfigEntry<bool> debug;

        public static float Probability {
            get { return probability.Value; }
            set { probability.Value = value; }
        }
        private static ConfigEntry<float> probability;

        public static float DistancePerRoll {
            get { return distancePerRoll.Value; }
            set { distancePerRoll.Value = value; }
        }
        private static ConfigEntry<float> distancePerRoll;

        public static float Force {
            get { return force.Value; }
            set { force.Value = value; }
        }
        private static ConfigEntry<float> force;

        public static bool MakeNoise {
            get { return makeNoise.Value; }
            set { makeNoise.Value = value; }
        }
        private static ConfigEntry<bool> makeNoise;
    }
}