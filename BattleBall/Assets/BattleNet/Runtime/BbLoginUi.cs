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

        const string KeyUser = BbSession.KeyUser, KeyToken = BbSession.KeyToken;
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
            LoadSaved();
        }

        void Start()
        {
            StartConnect();
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

        void OnDestroy() { _client?.Dispose(); }

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
            _savedUser = PlayerPrefs.GetString(KeyUser, "");
            _savedToken = PlayerPrefs.GetString(KeyToken, "");
        }

        bool HasSaved { get { return _savedUser.Length > 0 && _savedToken.Length > 0; } }

        void SaveAccount(string user, string token)
        {
            _savedUser = user;
            _savedToken = token;
            PlayerPrefs.SetString(KeyUser, user);
            PlayerPrefs.SetString(KeyToken, token);
            PlayerPrefs.Save();
        }

        void ClearSavedData()
        {
            PlayerPrefs.DeleteKey(KeyUser);
            PlayerPrefs.DeleteKey(KeyToken);
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
                await client.ConnectAsync(cfg.gameHost, cfg.gamePort, cfg.AcceptAnyCertificate, Application.version, Application.platform.ToString(), p => _target = p * 0.2f);          // the first fifth of the bar is the connection, the rest the asset download
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
            if (!string.IsNullOrEmpty(info.AssetBaseUrl))
            {
                try { await UpdateAssets(info.AssetBaseUrl, info.AssetVersion, ct); }
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
            if (HasSaved) ShowMenu(); else ShowForm(false);
        }

        async Task UpdateAssets(string baseUrl, string version, CancellationToken ct)
        {
            loadingStatus.text = "Đang kiểm tra tài nguyên...";
            var updater = new BbAssetUpdater(BbAssetCache.DefaultRoot);
            var result = await updater.UpdateAsync(baseUrl, version, BbAssetCache.Scale, (done, total) =>
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

        // ---------------------------------------------------------------------------------------------------------------- screens

        void ShowOnly(GameObject root)
        {
            loadingRoot.SetActive(root == loadingRoot);
            menuRoot.SetActive(root == menuRoot);
            formRoot.SetActive(root == formRoot);
            clearDataButton.gameObject.SetActive(root == loadingRoot || root == menuRoot);
        }

        void ShowMenu()
        {
            ShowOnly(menuRoot);
            resumeLabel.text = "Chơi TK: " + _savedUser;
            serverLabel.text = "Máy chủ: " + (_serverName ?? BbConfig.Current.serverName);
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
