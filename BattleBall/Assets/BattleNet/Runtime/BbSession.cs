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

        public const string ServerLocal = "local", ServerVps = "vps";

        /// <summary>The server chosen on the login screen for this run ("local" or "vps"); null until the player has chosen. The saved account is kept per server.</summary>
        public static string ServerId { get; set; }
        public static string UserKey { get { return ServerId == ServerVps ? KeyUser + ".vps" : KeyUser; } }          // "local" keeps the old keys, so accounts saved before the choice existed still work
        public static string TokenKey { get { return ServerId == ServerVps ? KeyToken + ".vps" : KeyToken; } }

        public static BbClient Client { get; private set; }
        public static string Username { get; private set; }
        public static PlayerDataSnapshot Snapshot { get { return Client != null ? Client.Snapshot : null; } }
        public static bool IsActive { get { return Client != null; } }

        /// <summary>Why the last session ended, for the login screen to show (null when the player logged out).</summary>
        public static string EndReason { get; set; }

        /// <summary>
        /// The match token of the dungeon being played (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25): a lost connection takes the player to the login screen,
        /// which connects again and gives this token to the server (ResumeMatchRequest) instead of logging in; the match then goes on where it stopped (<see cref="ResumedMatch"/>).
        /// </summary>
        public static string DungeonToken { get; set; }
        public static string LastUsername { get; set; }
        public static MatchStarted ResumedMatch { get; set; }

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
            if (toLogin) { DungeonToken = null; ResumedMatch = null; }          // leaving on purpose: nothing to come back to (Begin also calls End, and must keep a resumed match)
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
                    if (c.LastDisconnect != null && c.LastDisconnect.Reason == DisconnectReason.LoggedInElsewhere) DungeonToken = null;
                    else if (DungeonToken != null) EndReason = "Mất kết nối. Đang vào lại phó bản...";
                    LastUsername = Username;
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
