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

            cooldown = configFile.Bind(
                "Settings",
                "cooldown",
                15f,
                "Cooldown between drops in seconds.");

            heavyItemProbability = configFile.Bind(
                "Settings",
                "heavyItemProbability",
                0.1f,
                "Probability that you fumble each roll.");

            resourceProbability = configFile.Bind(
                "Settings",
                "resourceProbability",
                0.05f,
                "Probability that you fumble each roll.");

            consumableProbability = configFile.Bind(
                "Settings",
                "consumableProbability",
                0.05f,
                "Probability that you fumble each roll.");

            itemInPocketProbability = configFile.Bind(
                "Settings",
                "itemInPocketProbability",
                0.01f,
                "Probability that you fumble each roll.");

            distancePerRoll = configFile.Bind(
                "Settings",
                "distancePerRoll",
                10f,
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

        public static float Cooldown {
            get { return cooldown.Value; }
            set { cooldown.Value = value; }
        }
        private static ConfigEntry<float> cooldown;

        public static bool Debug {
            get { return debug.Value; }
            set { debug.Value = value; }
        }
        private static ConfigEntry<bool> debug;

        public static float HeavyItemProbability {
            get { return heavyItemProbability.Value; }
            set { heavyItemProbability.Value = value; }
        }
        private static ConfigEntry<float> heavyItemProbability;

        public static float ResourceProbability {
            get { return resourceProbability.Value; }
            set { resourceProbability.Value = value; }
        }
        private static ConfigEntry<float> resourceProbability;

        public static float ConsumableProbability {
            get { return consumableProbability.Value; }
            set { consumableProbability.Value = value; }
        }
        private static ConfigEntry<float> consumableProbability;
        public static float ItemInPocketProbability {
            get { return itemInPocketProbability.Value; }
            set { itemInPocketProbability.Value = value; }
        }
        private static ConfigEntry<float> itemInPocketProbability;

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