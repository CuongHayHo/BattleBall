using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// Where the game server is and how to reach it: one asset, Resources/BbConfig.asset (menu BattleBall/Create Config), instead of values spread over
    /// scene components. Edit it per environment before a build. The asset URL and the display name of the server come from the server itself
    /// (HelloAck); <see cref="serverName"/> is only the name shown until or unless the server sends one.
    /// </summary>
    public sealed class BbConfig : ScriptableObject
    {
        [Header("Game server (TCP + TLS)")]
        public string gameHost = "127.0.0.1";
        public int gamePort = 7443;
        public string serverName = "BattleBall";

        [Header("Sprites")]
        [Tooltip("Sprite scale to download and draw: 2, 3 or 4 (x4 is the sharpest, 38,7 MB of costume sprites instead of 16 MB for x2); 0 = decided once per device from its resolution and memory")]
        public int assetScale = 3;

        [Header("Development")]
        [Tooltip("Accept the self-signed certificate of the development server. Only honoured in the Editor and in Development Builds; release builds always validate the certificate.")]
        public bool acceptAnyCertificate = true;

        static BbConfig _current;

        /// <summary>The configuration asset, or built-in defaults (local development server) when the asset is missing.</summary>
        public static BbConfig Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<BbConfig>("BbConfig");
                    if (_current == null) _current = CreateInstance<BbConfig>();
                }
                return _current;
            }
        }

        /// <summary>The flag above, forced off in release builds.</summary>
        public bool AcceptAnyCertificate { get { return acceptAnyCertificate && Debug.isDebugBuild; } }
    }
}
