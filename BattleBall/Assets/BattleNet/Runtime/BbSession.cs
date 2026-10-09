using BattleBall.Proto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattleBall.Net
{
    /// <summary>
    /// The logged-in connection, kept while scenes change: the login screen hands its <see cref="BbClient"/> over with <see cref="Begin"/>, the menu and
    /// later scenes read <see cref="Snapshot"/> and send requests through <see cref="Client"/>. A small hidden object pumps the pushed events every frame
    /// and sends the player back to the login scene when the connection is lost.
    /// </summary>
    public static class BbSession
    {
        public const string LoginScene = "Login";
        public const string MenuScene = "Menu";
        public const string KeyUser = "bb.user", KeyToken = "bb.token";      // PlayerPrefs keys of the saved account (login screen and password change)

        public static BbClient Client { get; private set; }
        public static string Username { get; private set; }
        public static PlayerDataSnapshot Snapshot { get { return Client != null ? Client.Snapshot : null; } }
        public static bool IsActive { get { return Client != null; } }

        /// <summary>Why the last session ended, for the login screen to show (null when the player logged out).</summary>
        public static string EndReason { get; set; }

        public static void Begin(BbClient client, string username)
        {
            End(false);
            Client = client;
            Username = username;
            EndReason = null;
            var go = new GameObject("BbSessionPump");
            go.hideFlags = HideFlags.HideInHierarchy;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<BbSessionPump>();
        }

        /// <summary>Closes the connection. <paramref name="toLogin"/> also loads the login scene.</summary>
        public static void End(bool toLogin)
        {
            var c = Client;
            Client = null;
            Username = null;
            if (c != null) c.Logout();
            var pump = Object.FindObjectOfType<BbSessionPump>();
            if (pump != null) Object.Destroy(pump.gameObject);
            if (toLogin) SceneManager.LoadScene(LoginScene);
        }

        sealed class BbSessionPump : MonoBehaviour
        {
            void Update()
            {
                var c = Client;
                if (c == null) return;
                c.Pump();
                if (c.Dropped)
                {
                    EndReason = c.LastDisconnect != null && c.LastDisconnect.Reason == DisconnectReason.LoggedInElsewhere
                        ? "Tài khoản đã đăng nhập ở nơi khác"
                        : "Mất kết nối với máy chủ";
                    Client = null;
                    Username = null;
                    c.Dispose();
                    Destroy(gameObject);
                    SceneManager.LoadScene(LoginScene);
                }
            }
        }
    }
}
