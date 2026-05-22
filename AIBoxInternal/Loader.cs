using UnityEngine;
using NeoModLoader.api;

namespace AIBoxInternal
{
    // This is the guaranteed entry point NML will find
    public class ModEntry : BasicMod<ModEntry>
    {
        private GameObject _root;

        protected override void OnModLoad()
        {
            Debug.Log("[AIBoxInternal] Mod is loading...");
            _root = new GameObject("AIBoxInternal_Root");
            _root.AddComponent<MainController>();
            UnityEngine.Object.DontDestroyOnLoad(_root);
            Debug.Log("[AIBoxInternal] Mod loaded successfully!");
        }
    }

    public class MainController : MonoBehaviour
    {
        public static MainController Instance { get; private set; }
        public UI.ImGuiRenderer Renderer { get; private set; }
        public Core.AIBoxEngine Engine { get; private set; }

        void Awake()
        {
            Instance = this;
            Renderer = gameObject.AddComponent<UI.ImGuiRenderer>();
            gameObject.AddComponent<UI.NotificationManager>();
            gameObject.AddComponent<Core.AIProviderClient>();
            Engine = new Core.AIBoxEngine();
            Core.MailRegistry.Reset(); // Clear stale mail from previous sessions
        }

        void Start()
        {
            Hooks.HookManager.Install();
        }

        void Update()
        {
            Engine.Update();
            if (Input.GetKeyDown(KeyCode.Insert))
            {
                Renderer.enabled = !Renderer.enabled;
            }
        }

        void OnDestroy()
        {
            Hooks.HookManager.Uninstall();
            Debug.Log("[AIBoxInternal] Mod unloaded, Harmony patches removed.");
        }
    }
}
