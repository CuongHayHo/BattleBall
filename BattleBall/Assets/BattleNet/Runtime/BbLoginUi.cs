using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BattleBall.Proto;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// Start screens in the layout of the Nro client: loading (logo, progress bar, cancel), saved-account menu, login, register, and a placeholder
    /// "logged in" panel. The hierarchy is built by BattleBall/Build Login Scene (Editor/BbLoginSceneBuilder.cs); this component holds the references
    /// and the behaviour. The rules for names and passwords are the server's (AccountRules).
    /// Flow: connect (loading screen) -> saved account? menu : login form -> logged in.
    /// </summary>
    public sealed class BbLoginUi : MonoBehaviour
    {
        [Header("Screens (set by the scene builder)")]
        public GameObject loadingRoot, menuRoot, formRoot;
        public Text versionLabel;
        public Button clearDataButton;

        [Header("Loading")]
        public Image barFill;
        public Text loadingStatus;
        public Button cancelButton;
        public Text cancelLabel;

        [Header("Saved-account menu")]
        public Button resumeButton, newPlayButton, switchButton;
        public Text resumeLabel, serverLabel;
        public Image serverDot;

        [Header("Login / register form")]
        public RectTransform panel, buttonRow;
        public GameObject confirmRow;
        public InputField userField, passField, confirmField;
        public Text status;
        public Button okButton, secondButton;
        public Text secondLabel;

        const string KeyServer = "bb.server";            // the server chosen last time ("local" or "vps"), marked on the next start
        static readonly Regex UsernameRule = new Regex("^[a-z0-9_]{4,20}$");
        static readonly Color Error = new Color(0.72f, 0.12f, 0.08f), Good = new Color(0.12f, 0.45f, 0.15f);
        static readonly Color DotOn = new Color(0.2f, 0.9f, 0.2f), DotOff = new Color(0.9f, 0.2f, 0.2f);

        bool _registerMode, _busy;
        int _attempt;                       // bumped by every connect and by Cancel: an answer from an older attempt is dropped
        float _target, _shown;
        string _serverName;                 // from HelloAck, else from BbConfig
        BbClient _client;
        CancellationTokenSource _cts;       // cancels the asset download of the running attempt
        string _savedUser, _savedToken;

        // ---------------------------------------------------------------------------------------------------------------- lifecycle

        void Awake()
        {
            versionLabel.text = "v" + Application.version;
            cancelButton.onClick.AddListener(OnCancelOrRetry);
            clearDataButton.onClick.AddListener(ClearSavedData);
            resumeButton.onClick.AddListener(ResumeSaved);
            newPlayButton.onClick.AddListener(() => ShowForm(true));
            switchButton.onClick.AddListener(() => ShowForm(false));
            okButton.onClick.AddListener(Submit);
            secondButton.onClick.AddListener(() => ShowForm(!_registerMode));
            BuildServerChooser();
        }

        void Start()
        {
            if (BbSession.ServerId == null) ShowServerChooser();          // first screen of a run: Local or VPS; coming back after a lost connection keeps the server
            else { LoadSaved(); StartConnect(); }
            if (BbSession.EndReason != null)                // the session just ended (connection lost, logged in elsewhere): say why
            {
                loadingStatus.text = BbSession.EndReason;
                BbSession.EndReason = null;
            }
        }

        void Update()
        {
            _client?.Pump();
            _shown = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * 2f);
            barFill.fillAmount = _shown;
            CheckDropped();
        }

        void OnDestroy()
        {
            _attempt++;                                    // a connect or download still running must not touch the texts of this destroyed screen when it ends
            _cts?.Cancel();
            _client?.Dispose();
        }

        // ---------------------------------------------------------------------------------------------------------------- rules (same as the server)

        /// <summary>Returns a message for the first problem, or null when the input is acceptable.</summary>
        public static string Validate(string user, string pass, string confirm, bool registering)
        {
            if (!UsernameRule.IsMatch(user ?? "")) return "Tên tài khoản gồm 4 đến 20 ký tự: a-z, 0-9 và _";
            int bytes = Encoding.UTF8.GetByteCount(pass ?? "");
            if (bytes < 8 || bytes > 64) return "Mật khẩu dài từ 8 đến 64 ký tự";
            if (registering && pass != confirm) return "Hai mật khẩu không giống nhau";
            return null;
        }

        public static string Explain(RegisterError e)
        {
            switch (e)
            {
                case RegisterError.UsernameInvalid: return "Tên tài khoản không hợp lệ";
                case RegisterError.PasswordInvalid: return "Mật khẩu không hợp lệ";
                case RegisterError.UsernameTaken: return "Tên tài khoản đã có người dùng";
                case RegisterError.RegisterRateLimited: return "Đăng ký quá nhiều lần, hãy thử lại sau";
                default: return "Máy chủ gặp lỗi, hãy thử lại sau";
            }
        }

        public static string Explain(LoginError e)
        {
            switch (e)
            {
                case LoginError.AuthFailed: return "Sai tên tài khoản hoặc mật khẩu";
                case LoginError.AuthRateLimited: return "Nhập sai quá nhiều lần, hãy thử lại sau";
                case LoginError.AccountBanned: return "Tài khoản đã bị khóa";
                case LoginError.TokenExpired:
                case LoginError.TokenRevoked: return "Phiên đăng nhập hết hạn, hãy đăng nhập lại";
                default: return "Máy chủ gặp lỗi, hãy thử lại sau";
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- saved account

        void LoadSaved()
        {
            _savedUser = PlayerPrefs.GetString(BbSession.UserKey, "");
            _savedToken = PlayerPrefs.GetString(BbSession.TokenKey, "");
        }

        bool HasSaved { get { return _savedUser.Length > 0 && _savedToken.Length > 0; } }

        void SaveAccount(string user, string token)
        {
            _savedUser = user;
            _savedToken = token;
            PlayerPrefs.SetString(BbSession.UserKey, user);
            PlayerPrefs.SetString(BbSession.TokenKey, token);
            PlayerPrefs.Save();
        }

        void ClearSavedData()
        {
            PlayerPrefs.DeleteKey(BbSession.UserKey);
            PlayerPrefs.DeleteKey(BbSession.TokenKey);
            PlayerPrefs.Save();
            LoadSaved();
            if (menuRoot.activeSelf) ShowForm(false);
        }

        // ---------------------------------------------------------------------------------------------------------------- loading screen

        async void StartConnect()
        {
            int attempt = ++_attempt;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _client?.Dispose();
            _client = null;
            ShowOnly(loadingRoot);
            clearDataButton.gameObject.SetActive(true);
            cancelLabel.text = "Hủy";
            loadingStatus.text = "Đang kết nối máy chủ...";
            loadingStatus.color = Color.white;
            _shown = _target = 0f;
            var client = new BbClient();
            try
            {
                var cfg = BbConfig.Current;
                bool vps = BbSession.ServerId == BbSession.ServerVps;
                // the VPS has a real certificate (Let's Encrypt): always validated; only the Local development server may be self-signed
                await client.ConnectAsync(vps ? cfg.vpsHost : cfg.gameHost, vps ? cfg.vpsPort : cfg.gamePort, !vps && cfg.AcceptAnyCertificate, Application.version, Application.platform.ToString(), p => _target = p * 0.2f);          // the first fifth of the bar is the connection, the rest the asset download
            }
            catch (Exception e)
            {
                client.Dispose();
                if (attempt != _attempt) return;           // cancelled meanwhile
                loadingStatus.text = e is TimeoutException ? "Không kết nối được máy chủ (quá thời gian chờ)" : "Không kết nối được máy chủ";
                loadingStatus.color = new Color(1f, 0.6f, 0.5f);
                cancelLabel.text = "Thử lại";
                return;
            }
            if (attempt != _attempt) { client.Dispose(); return; }

            // game assets announced by the server (HelloAck): download what is missing or changed, with the progress on the same bar
            var info = client.Connection.ServerInfo;
            if (!string.IsNullOrEmpty(info.AssetVersion))
            {
                try { await UpdateAssets(client.Connection, info.AssetVersion, ct); }
                catch (OperationCanceledException) { client.Dispose(); return; }
                catch (Exception e)
                {
                    client.Dispose();
                    if (attempt != _attempt) return;
                    loadingStatus.text = "Không tải được tài nguyên: " + e.Message;
                    loadingStatus.color = new Color(1f, 0.6f, 0.5f);
                    cancelLabel.text = "Thử lại";
                    return;
                }
                if (attempt != _attempt) { client.Dispose(); return; }
            }
            _client = client;
            _serverName = string.IsNullOrEmpty(info.ServerName) ? BbConfig.Current.serverName : info.ServerName;
            _shown = _target = 1f;
            // a dungeon was being played when the connection was lost: the match token takes the player back into it, without a login
            if (BbSession.DungeonToken != null && !string.IsNullOrEmpty(BbSession.LastUsername))
            {
                loadingStatus.text = "Đang vào lại phó bản...";
                if (await TryResumeDungeon(client))
                {
                    try { await EnterMenu(BbSession.LastUsername); return; }
                    catch (Exception) { BbSession.ResumedMatch = null; }
                }
                if (attempt != _attempt) return;
            }
            if (HasSaved) ShowMenu(); else ShowForm(false);
        }

        /// <summary>The server still holds the match: it answers with the whole picture of it (MatchResumed) and the player data follows.</summary>
        async Task<bool> TryResumeDungeon(BbClient client)
        {
            try
            {
                var answer = await client.Connection.RequestAsync(new Envelope { ResumeMatchRequest = new ResumeMatchRequest { MatchToken = BbSession.DungeonToken } });
                if (answer.PayloadCase == Envelope.PayloadOneofCase.MatchResumed && answer.MatchResumed.Ok)
                {
                    BbSession.ResumedMatch = answer.MatchResumed.Match;
                    return true;
                }
            }
            catch (Exception) { }
            BbSession.DungeonToken = null;                  // the match is gone (it ended, or the time to come back ran out)
            BbSession.ResumedMatch = null;
            return false;
        }

        async Task UpdateAssets(BbConnection connection, string version, CancellationToken ct)
        {
            loadingStatus.text = "Đang kiểm tra tài nguyên...";
            var updater = new BbAssetUpdater(BbAssetCache.DefaultRoot);
            var result = await updater.UpdateAsync(connection, version, BbAssetCache.Scale, (done, total) =>
            {
                _target = 0.2f + 0.8f * (total > 0 ? Mathf.Clamp01(done / (float)total) : 1f);
                loadingStatus.text = total > 0
                    ? "Đang tải tài nguyên " + (done / 1048576f).ToString("F1") + " / " + (total / 1048576f).ToString("F1") + " MB"
                    : "Đang kiểm tra tài nguyên...";
            }, ct);
            _target = 1f;
            loadingStatus.text = result.wasUpToDate || result.packsDownloaded == 0 ? "Tài nguyên đã mới nhất" : "Đã tải xong tài nguyên";
        }

        void OnCancelOrRetry()
        {
            if (cancelLabel.text == "Hủy")
            {
                _attempt++;
                _cts?.Cancel();                                // drops the answer of the running attempt
                loadingStatus.text = "Đã hủy kết nối";
                loadingStatus.color = Color.white;
                cancelLabel.text = "Thử lại";
                _target = 0f;
            }
            else
            {
                StartConnect();
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- server choice (Local or VPS)

        GameObject _chooserRoot;
        Button _changeServerButton;

        /// <summary>Built by code on the canvas of this scene, so the Login scene itself does not change.</summary>
        void BuildServerChooser()
        {
            var parent = (RectTransform)versionLabel.canvas.transform;
            _chooserRoot = new GameObject("ServerChooser", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)_chooserRoot.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _chooserRoot.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.13f, 0.97f);
            MakeText(rt, "Chọn máy chủ", 34, new Vector2(0f, 150f), new Vector2(600f, 60f));
            string last = PlayerPrefs.GetString(KeyServer, "");
            MakeButton(rt, "Chơi Local" + (last == BbSession.ServerLocal ? "  (lần trước)" : ""), new Vector2(0f, 50f), new Vector2(460f, 76f), () => ChooseServer(BbSession.ServerLocal), new Color(0.2f, 0.45f, 0.75f));
            MakeButton(rt, "Chơi VPS" + (last == BbSession.ServerVps ? "  (lần trước)" : ""), new Vector2(0f, -50f), new Vector2(460f, 76f), () => ChooseServer(BbSession.ServerVps), new Color(0.18f, 0.55f, 0.28f));
            _chooserRoot.SetActive(false);

            _changeServerButton = MakeButton(parent, "Đổi máy chủ", Vector2.zero, new Vector2(190f, 48f), ShowServerChooser, new Color(0f, 0f, 0f, 0.55f));
            var crt = (RectTransform)_changeServerButton.transform;
            crt.anchorMin = crt.anchorMax = Vector2.zero;                // bottom-left corner
            crt.anchoredPosition = new Vector2(110f, 36f);
            _changeServerButton.gameObject.SetActive(false);
        }

        Text MakeText(RectTransform parent, string text, int size, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = box;
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = versionLabel.font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        Button MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = color;
            var b = go.GetComponent<Button>();
            b.onClick.AddListener(onClick);
            MakeText(rt, label, size.y > 60f ? 28 : 20, Vector2.zero, size);
            return b;
        }

        /// <summary>Drops the running connection (if any) and shows the choice.</summary>
        void ShowServerChooser()
        {
            if (_busy) return;
            _attempt++;
            _cts?.Cancel();
            _client?.Dispose();
            _client = null;
            ShowOnly(null);
        }

        void ChooseServer(string id)
        {
            if (BbSession.ServerId != id)
            {
                BbSession.DungeonToken = null;                  // a match token and the player of another server mean nothing here
                BbSession.LastUsername = null;
                BbSession.ResumedMatch = null;
                userField.text = passField.text = confirmField.text = "";
            }
            BbSession.ServerId = id;
            PlayerPrefs.SetString(KeyServer, id);
            PlayerPrefs.Save();
            LoadSaved();
            StartConnect();
        }

        // ---------------------------------------------------------------------------------------------------------------- screens

        void ShowOnly(GameObject root)
        {
            loadingRoot.SetActive(root == loadingRoot);
            menuRoot.SetActive(root == menuRoot);
            formRoot.SetActive(root == formRoot);
            clearDataButton.gameObject.SetActive(root == loadingRoot || root == menuRoot);
            if (_chooserRoot != null) _chooserRoot.SetActive(root == null);
            if (_changeServerButton != null) _changeServerButton.gameObject.SetActive(root != null);
        }

        void ShowMenu()
        {
            ShowOnly(menuRoot);
            resumeLabel.text = "Chơi TK: " + _savedUser;
            serverLabel.text = "Máy chủ: " + (_serverName ?? BbConfig.Current.serverName) + (BbSession.ServerId == BbSession.ServerVps ? " (VPS)" : " (Local)");
            serverDot.color = _client != null && _client.Connection.IsConnected ? DotOn : DotOff;
        }

        void ShowForm(bool registering)
        {
            if (_busy) return;
            ShowOnly(formRoot);
            _registerMode = registering;
            confirmRow.SetActive(registering);
            secondLabel.text = registering ? "Quay lại" : "Đăng ký";
            // the panel grows by one row in register mode; the buttons stay just under it
            float h = registering ? 306f : 230f;
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, h);
            buttonRow.anchoredPosition = new Vector2(0f, panel.anchoredPosition.y - h - 20f);
            status.rectTransform.anchoredPosition = new Vector2(0f, registering ? -246f : -174f);
            if (!registering && userField.text.Length == 0) userField.text = _savedUser;
            Say("", Error);
        }

        void Say(string text, Color color) { status.text = text; status.color = color; }

        void SetBusy(bool busy)
        {
            _busy = busy;
            okButton.interactable = !busy;
            secondButton.interactable = !busy;
            resumeButton.interactable = !busy;
            newPlayButton.interactable = !busy;
            switchButton.interactable = !busy;
            userField.interactable = !busy;
            passField.interactable = !busy;
            confirmField.interactable = !busy;
        }

        // ---------------------------------------------------------------------------------------------------------------- login / register

        async void Submit()
        {
            if (_busy || _client == null) return;
            string user = userField.text.Trim(), pass = passField.text;
            string problem = Validate(user, pass, confirmField.text, _registerMode);
            if (problem != null) { Say(problem, Error); return; }

            SetBusy(true);
            try
            {
                if (_registerMode)
                {
                    Say("Đang đăng ký...", Good);
                    var reg = await _client.RegisterAsync(user, pass);
                    if (!reg.Ok) { Say(Explain(reg.Error), Error); return; }
                }
                Say("Đang đăng nhập...", Good);
                var login = await _client.LoginWithPasswordAsync(user, pass, SystemInfo.deviceModel);
                if (!login.Ok) { Say(Explain(login.Error), Error); return; }
                if (!string.IsNullOrEmpty(login.RefreshToken)) SaveAccount(login.Username, login.RefreshToken);
                passField.text = "";
                confirmField.text = "";
                await EnterMenu(login.Username);
            }
            catch (Exception)
            {
                Say("Mất kết nối với máy chủ", Error);
                StartConnect();
            }
            finally { SetBusy(false); }
        }

        async void ResumeSaved()
        {
            if (_busy || _client == null) return;
            SetBusy(true);
            try
            {
                var login = await _client.LoginWithTokenAsync(_savedUser, _savedToken, SystemInfo.deviceModel);
                if (!login.Ok)
                {
                    if (login.Error == LoginError.TokenExpired || login.Error == LoginError.TokenRevoked) ClearSavedData();
                    ShowForm(false);
                    Say(Explain(login.Error), Error);
                    return;
                }
                await EnterMenu(login.Username);
            }
            catch (Exception) { StartConnect(); }
            finally { SetBusy(false); }
        }

        /// <summary>Logged in: wait for the player data the server pushes right after login, hand the connection to <see cref="BbSession"/>, open the menu.</summary>
        async Task EnterMenu(string username)
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (_client.Snapshot == null && !_client.Dropped && DateTime.UtcNow < until) await Task.Yield();
            if (_client.Snapshot == null) throw new TimeoutException("no player data");
            var client = _client;
            _client = null;                                 // the session owns the connection now
            BbSession.Begin(client, username);
            SceneManager.LoadScene(BbSession.MenuScene);
        }

        void CheckDropped()
        {
            if (_client == null || !_client.Dropped || _busy) return;
            bool elsewhere = _client.LastDisconnect != null && _client.LastDisconnect.Reason == DisconnectReason.LoggedInElsewhere;
            StartConnect();
            if (elsewhere) loadingStatus.text = "Tài khoản đã đăng nhập ở nơi khác";
        }
    }
}
