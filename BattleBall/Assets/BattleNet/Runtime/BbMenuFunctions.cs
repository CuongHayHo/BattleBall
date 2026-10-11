using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The "Chức năng" tab of the menu: Hòm thư (gifts from the admin), Thông báo and Tài khoản (change the password, log out). It fills the panel under the tabs;
    /// the mail and the notice open in the shared <see cref="BbPopup"/>. Mails and notices are asked from the server when their list is opened; the server also
    /// pushes a fresh mail list after every mail action. The hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbMenuFunctions : MonoBehaviour
    {
        [Header("Tab")]
        public GameObject root;
        public Text headerText;

        [Header("Menu view")]
        public GameObject menuView;
        public Button mailButton, noticeButton, accountButton;
        public GameObject mailBadge;
        public Text mailBadgeText;

        [Header("List view (mails and notices)")]
        public GameObject listView;
        public Text listTitle, emptyText, actionLabel;
        public Button backButton, actionButton;
        public RectTransform content;
        public GameObject rowTemplate;
        public ScrollRect scroll;

        [Header("Account view")]
        public GameObject accountView;
        public Text accountInfo;
        public Button changePasswordButton, logoutButton;

        [Header("Config view (sprite scale x1 to x4)")]
        public GameObject configView;
        public Button configButton;
        public Button[] scaleButtons;
        public Image[] scaleImages;
        public Text[] scaleLabels;
        public Text configStatus;
        public Button soundButton, weatherButton;                 // sound and weather on or off

        [Header("Password form")]
        public GameObject passwordRoot;
        public InputField oldField, newField, confirmField;
        public Text passwordStatus;
        public Button passwordOk, passwordCancel;

        [Header("Shared")]
        public BbPopup popup;
        public BbMenuUi menu;
        public Sprite goldSprite, gemSprite;

        enum View { Menu, Mails, Notices, Account, Config }

        View _view = View.Menu;
        PlayerDataSnapshot _snapshot;
        MailList _mails = new MailList();
        NoticeList _notices = new NoticeList();
        MailList _demoMails;
        NoticeList _demoNotices;
        bool _demo, _busy, _subscribed;
        readonly List<GameObject> _rows = new List<GameObject>();

        public bool IsOpen { get { return root != null && root.activeSelf; } }

        /// <summary>True while a sub view or the password form is open: a horizontal drag must not change the tab then.</summary>
        public bool BlocksSwipe { get { return root.activeSelf && (_view != View.Menu || passwordRoot.activeSelf); } }

        void Awake()
        {
            mailButton.onClick.AddListener(() => OpenView(View.Mails));
            noticeButton.onClick.AddListener(() => OpenView(View.Notices));
            accountButton.onClick.AddListener(() => OpenView(View.Account));
            configButton.onClick.AddListener(() => OpenView(View.Config));
            if (soundButton != null) soundButton.onClick.AddListener(() => { BbSfx.Enabled = !BbSfx.Enabled; RefreshToggles(); });
            if (weatherButton != null) weatherButton.onClick.AddListener(() => { BbWeather.Enabled = !BbWeather.Enabled; RefreshToggles(); });
            for (int i = 0; i < scaleButtons.Length; i++)
            {
                int scale = i + 1;
                scaleButtons[i].onClick.AddListener(() => ChooseScale(scale));
            }
            backButton.onClick.AddListener(() => OpenView(View.Menu));
            actionButton.onClick.AddListener(ClaimAll);
            changePasswordButton.onClick.AddListener(OpenPasswordForm);
            logoutButton.onClick.AddListener(() => BbSession.End(true));
            passwordOk.onClick.AddListener(SubmitPassword);
            passwordCancel.onClick.AddListener(() => passwordRoot.SetActive(false));
            rowTemplate.SetActive(false);
            passwordRoot.SetActive(false);
        }

        void Start()
        {
            Subscribe();
        }

        void Subscribe()
        {
            if (_subscribed || !BbSession.IsActive) return;
            _subscribed = true;
            BbSession.Client.EventReceived += OnEvent;
        }

        void OnDestroy()
        {
            if (_subscribed && BbSession.Client != null) BbSession.Client.EventReceived -= OnEvent;
        }

        void OnEvent(Envelope e)
        {
            if (e.PayloadCase != Envelope.PayloadOneofCase.MailList) return;
            _mails = e.MailList;
            if (root.activeSelf && _view == View.Mails) BuildMailRows();
        }

        // ---------------------------------------------------------------------------------------------------------------- tab

        public void Show()
        {
            Subscribe();
            root.SetActive(true);
            OpenView(View.Menu);
        }

        public void Hide()
        {
            root.SetActive(false);
            passwordRoot.SetActive(false);
        }

        /// <summary>The snapshot changed: the header and the unread badge.</summary>
        public void Refresh(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            if (snapshot == null) return;
            headerText.text = snapshot.Username + "\nHòm thư chưa đọc: " + snapshot.UnreadMail;
            mailBadge.SetActive(snapshot.UnreadMail > 0);
            mailBadgeText.text = snapshot.UnreadMail.ToString(CultureInfo.InvariantCulture);
            accountInfo.text = "Tài khoản: " + snapshot.Username;
        }

        /// <summary>For the screenshot tool: made-up mails and notices instead of the server's.</summary>
        public void SetDemo(MailList mails, NoticeList notices)
        {
            _demo = true;
            _demoMails = mails;
            _demoNotices = notices;
        }

        /// <summary>For the screenshot tool: 0 menu, 1 mails, 2 notices, 3 account, 4 password form.</summary>
        public void Open(int view)
        {
            Show();
            switch (view)
            {
                case 1: OpenView(View.Mails); break;
                case 2: OpenView(View.Notices); break;
                case 3: OpenView(View.Account); break;
                case 4: OpenView(View.Account); OpenPasswordForm(); break;
                case 5: OpenView(View.Config); break;
            }
        }

        /// <summary>For the screenshot tool: tap the mail / notice at this index of the open list.</summary>
        public void DemoOpenMail(int index) { OpenMail(_mails.Mails[index]); }

        public void DemoOpenNotice(int index)
        {
            var n = _notices.Notices[index];
            popup.Show(null, n.Title, "", n.Body, null, null, new List<BbPopup.ButtonSpec>(), null);
        }

        void OpenView(View view)
        {
            _view = view;
            menuView.SetActive(view == View.Menu);
            listView.SetActive(view == View.Mails || view == View.Notices);
            accountView.SetActive(view == View.Account);
            configView.SetActive(view == View.Config);
            if (view == View.Config)
            {
                RefreshScaleButtons();
                configStatus.text = "Đang dùng độ phân giải x" + BbAssetCache.Scale;
            }
            actionButton.gameObject.SetActive(false);
            backButton.gameObject.SetActive(view != View.Menu);
            if (view == View.Mails) LoadMails();
            else if (view == View.Notices) LoadNotices();
        }

        // ---------------------------------------------------------------------------------------------------------------- mails

        async void LoadMails()
        {
            listTitle.text = "Hòm thư";
            ClearRows();
            emptyText.text = "";
            if (_demo)
            {
                _mails = _demoMails;
            }
            else
            {
                if (!BbSession.IsActive) return;
                if (_busy) return;
                _busy = true;
                emptyText.text = "Đang tải...";
                try { _mails = await BbSession.Client.MailListAsync(); }
                catch (Exception) { emptyText.text = "Không tải được hòm thư"; _busy = false; return; }
                _busy = false;
            }
            if (_view == View.Mails) BuildMailRows();
        }

        void BuildMailRows()
        {
            ClearRows();
            bool anyGift = false;
            foreach (var mail in _mails.Mails)
            {
                var m = mail;
                bool hasGift = m.Attachments.Count > 0 && !m.Claimed;
                anyGift |= hasGift;
                string sub = SentAt(m.SentAtMs) + (hasGift ? "   Có quà" : m.Attachments.Count > 0 ? "   Đã nhận quà" : "");
                AddRow(m.Title, sub, !m.Read, hasGift, () => OpenMail(m));
            }
            emptyText.text = _mails.Mails.Count == 0 ? "Chưa có thư nào" : "";
            actionLabel.text = "Nhận tất cả";
            actionButton.gameObject.SetActive(anyGift);
            scroll.verticalNormalizedPosition = 1f;
        }

        void OpenMail(MailInfo mail)
        {
            if (!mail.Read) MarkRead(mail);
            var sprites = new List<Sprite>();
            var texts = new List<string>();
            var lines = new StringBuilder();
            foreach (var a in mail.Attachments)
            {
                sprites.Add(GiftSprite(a));
                texts.Add(GiftShort(a));
                if (lines.Length > 0) lines.Append('\n');
                lines.Append("<color=#1E8F1E>").Append(GiftText(a)).Append("</color>");
            }
            var specs = new List<BbPopup.ButtonSpec>();
            bool hasGift = mail.Attachments.Count > 0 && !mail.Claimed;
            if (hasGift) specs.Add(new BbPopup.ButtonSpec("Nhận quà", true, true));
            else specs.Add(new BbPopup.ButtonSpec("Xóa thư", true, false));
            string state = mail.Attachments.Count > 0 && mail.Claimed ? "\n(Đã nhận quà)" : "";
            popup.Show(null, mail.Title, lines.ToString(), mail.Body + state, sprites, texts, specs, i =>
            {
                popup.Hide();
                if (hasGift) Claim(mail); else Delete(mail);
            });
        }

        async void MarkRead(MailInfo mail)
        {
            if (_demo || !BbSession.IsActive) return;
            try { await BbSession.Client.MailActionAsync(mail.MailId, MailAction.MailRead); }
            catch (Exception) { /* the badge catches up at the next snapshot */ }
        }

        async void Claim(MailInfo mail)
        {
            if (_demo || !BbSession.IsActive) { menu.Toast("Đã nhận quà (thử nghiệm)"); return; }
            try
            {
                var r = await BbSession.Client.MailActionAsync(mail.MailId, MailAction.MailClaim);
                menu.Toast(r.Ok ? "Đã nhận quà" : BbMenuUi.ActionText(r));
            }
            catch (Exception e) { menu.Toast("Không nhận được quà: " + e.Message); }
        }

        async void ClaimAll()
        {
            if (_demo || !BbSession.IsActive) { menu.Toast("Đã nhận tất cả (thử nghiệm)"); return; }
            try
            {
                var r = await BbSession.Client.MailActionAsync(0, MailAction.MailClaimAll);
                menu.Toast(r.Ok ? "Đã nhận " + r.NewId + " thư quà" : BbMenuUi.ActionText(r));
            }
            catch (Exception e) { menu.Toast("Không nhận được quà: " + e.Message); }
        }

        async void Delete(MailInfo mail)
        {
            if (_demo || !BbSession.IsActive) { menu.Toast("Đã xóa thư (thử nghiệm)"); return; }
            try
            {
                var r = await BbSession.Client.MailActionAsync(mail.MailId, MailAction.MailDelete);
                if (!r.Ok) menu.Toast(BbMenuUi.ActionText(r));
            }
            catch (Exception e) { menu.Toast("Không xóa được: " + e.Message); }
        }

        // ---------------------------------------------------------------------------------------------------------------- notices

        async void LoadNotices()
        {
            listTitle.text = "Thông báo";
            ClearRows();
            emptyText.text = "";
            if (_demo)
            {
                _notices = _demoNotices;
            }
            else
            {
                if (!BbSession.IsActive) return;
                if (_busy) return;
                _busy = true;
                emptyText.text = "Đang tải...";
                try { _notices = await BbSession.Client.NoticeListAsync(); }
                catch (Exception) { emptyText.text = "Không tải được thông báo"; _busy = false; return; }
                _busy = false;
            }
            if (_view != View.Notices) return;
            ClearRows();
            foreach (var notice in _notices.Notices)
            {
                var n = notice;
                AddRow(n.Title, SentAt(n.CreatedAtMs), false, false, () => popup.Show(null, n.Title, "", n.Body, null, null, new List<BbPopup.ButtonSpec>(), null));
            }
            emptyText.text = _notices.Notices.Count == 0 ? "Chưa có thông báo" : "";
            scroll.verticalNormalizedPosition = 1f;
        }

        // ---------------------------------------------------------------------------------------------------------------- list rows

        void ClearRows()
        {
            foreach (var r in _rows) Destroy(r);
            _rows.Clear();
        }

        void AddRow(string title, string sub, bool unread, bool gift, Action onTap)
        {
            var row = Instantiate(rowTemplate, content);
            row.SetActive(true);
            row.name = "Row";
            row.transform.Find("Title").GetComponent<Text>().text = title;
            row.transform.Find("Sub").GetComponent<Text>().text = sub;
            row.transform.Find("Dot").gameObject.SetActive(unread);
            row.transform.Find("Gift").gameObject.SetActive(gift);
            row.GetComponent<Button>().onClick.AddListener(() => onTap());
            _rows.Add(row);
        }

        // ---------------------------------------------------------------------------------------------------------------- config: the sprite scale

        int _demoScale = 3;

        int CurrentScale { get { return _demo ? _demoScale : BbAssetCache.Scale; } }

        void RefreshToggles()
        {
            if (soundButton != null) soundButton.GetComponentInChildren<Text>().text = "Âm thanh: " + (BbSfx.Enabled ? "Bật" : "Tắt");
            if (weatherButton != null) weatherButton.GetComponentInChildren<Text>().text = "Thời tiết: " + (BbWeather.Enabled ? "Bật" : "Tắt");
        }

        void RefreshScaleButtons()
        {
            RefreshToggles();
            for (int i = 0; i < scaleButtons.Length; i++)
            {
                bool on = i + 1 == CurrentScale;
                scaleImages[i].sprite = on ? popup.primarySprite : popup.normalSprite;
                scaleLabels[i].color = new Color(0.30f, 0.14f, 0.05f);             // dark on the green and the orange button, as the Nro commands
                scaleButtons[i].interactable = !_busy;
            }
        }

        /// <summary>
        /// Switches the sprite resolution (x1 to x4): the choice is saved, the packs of that scale are downloaded from the server when they are missing
        /// (with the progress shown), then every picture of the menu and the village is drawn again.
        /// </summary>
        async void ChooseScale(int scale)
        {
            if (_busy) return;
            if (scale == CurrentScale) { configStatus.text = "Đang dùng độ phân giải x" + scale; return; }
            if (_demo) { _demoScale = scale; RefreshScaleButtons(); configStatus.text = "Đã chuyển sang x" + scale + " (thử nghiệm)"; return; }
            int previous = BbAssetCache.Scale;
            _busy = true;
            RefreshScaleButtons();
            BbAssetCache.SetUserScale(scale);
            try
            {
                string version = BbSession.IsActive ? BbSession.Client.Connection.ServerInfo.AssetVersion : "";
                if (!string.IsNullOrEmpty(version))
                {
                    configStatus.text = "Đang kiểm tra tài nguyên x" + scale + "...";
                    var updater = new BbAssetUpdater(BbAssetCache.DefaultRoot);
                    await updater.UpdateAsync(BbSession.Client.Connection, version, scale, (done, total) =>
                    {
                        if (total > 0) configStatus.text = "Đang tải x" + scale + ": " + (done / 1048576f).ToString("F1") + " / " + (total / 1048576f).ToString("F1") + " MB";
                    }, CancellationToken.None);
                }
                menu.ApplyScaleChange();
                configStatus.text = "Đã chuyển sang độ phân giải x" + scale;
            }
            catch (Exception e)
            {
                BbAssetCache.SetUserScale(previous);                  // keep what works
                configStatus.text = "Không đổi được: " + e.Message;
            }
            _busy = false;
            RefreshScaleButtons();
        }

        // ---------------------------------------------------------------------------------------------------------------- account

        void OpenPasswordForm()
        {
            oldField.text = newField.text = confirmField.text = "";
            passwordStatus.text = "";
            passwordRoot.SetActive(true);
        }

        async void SubmitPassword()
        {
            if (_busy) return;
            string oldPw = oldField.text, newPw = newField.text;
            if (oldPw.Length == 0) { SetStatus("Nhập mật khẩu hiện tại", true); return; }
            int bytes = Encoding.UTF8.GetByteCount(newPw);
            if (bytes < 8 || bytes > 64) { SetStatus("Mật khẩu mới dài từ 8 đến 64 ký tự", true); return; }
            if (newPw != confirmField.text) { SetStatus("Nhập lại mật khẩu mới chưa khớp", true); return; }
            if (newPw == oldPw) { SetStatus("Mật khẩu mới phải khác mật khẩu cũ", true); return; }
            if (_demo || !BbSession.IsActive) { SetStatus("Đã đổi (thử nghiệm)", false); return; }
            _busy = true;
            SetStatus("Đang gửi...", false);
            try
            {
                var r = await BbSession.Client.ChangePasswordAsync(oldPw, newPw);
                if (r.Ok)
                {
                    PlayerPrefs.SetString(BbSession.UserKey, BbSession.Username ?? "");            // the old tokens were revoked: save the new one
                    PlayerPrefs.SetString(BbSession.TokenKey, r.RefreshToken);
                    PlayerPrefs.Save();
                    passwordRoot.SetActive(false);
                    menu.Toast("Đã đổi mật khẩu");
                }
                else
                {
                    SetStatus(PasswordError(r.Error), true);
                }
            }
            catch (Exception e) { SetStatus("Không gửi được: " + e.Message, true); }
            _busy = false;
        }

        static string PasswordError(ChangePasswordError e)
        {
            switch (e)
            {
                case ChangePasswordError.WrongOldPassword: return "Mật khẩu hiện tại không đúng";
                case ChangePasswordError.NewPasswordInvalid: return "Mật khẩu mới không hợp lệ (8 đến 64 ký tự)";
                case ChangePasswordError.ChangePasswordRateLimited: return "Nhập sai quá nhiều lần, hãy thử lại sau";
                default: return "Máy chủ gặp lỗi, hãy thử lại sau";
            }
        }

        void SetStatus(string text, bool error)
        {
            passwordStatus.text = text;
            passwordStatus.color = error ? new Color(0.72f, 0.12f, 0.08f) : new Color(0.12f, 0.45f, 0.15f);
        }

        // ---------------------------------------------------------------------------------------------------------------- texts and pictures

        static string SentAt(ulong ms)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture); }
            catch (Exception) { return ""; }
        }

        static string Number(ulong v)
        {
            return v.ToString("N0", new CultureInfo("vi-VN"));
        }

        static string GiftText(MailAttachment a)
        {
            switch (a.Kind)
            {
                case "GOLD": return Number(a.Quantity) + " vàng";
                case "GEMS": return Number(a.Quantity) + " đá quý";
                case "SKIN":
                case "EARRING": return a.Name;
                default: return a.Name + " x" + Number(a.Quantity);
            }
        }

        static string GiftShort(MailAttachment a)
        {
            switch (a.Kind)
            {
                case "GOLD":
                case "GEMS": return Number(a.Quantity);
                case "SKIN":
                case "EARRING": return "";
                default: return "x" + Number(a.Quantity);
            }
        }

        Sprite GiftSprite(MailAttachment a)
        {
            if (a.Kind == "GOLD") return goldSprite;
            if (a.Kind == "GEMS") return gemSprite;
            if (a.Kind == "SKIN") return BbCostumeAvatar.Of((int)a.RefId);
            var resolver = NroSpriteResolver.Instance;
            if (resolver == null) return null;
            int iconId = (int)a.IconId;
            return iconId > 0 ? resolver.ResolveSprite(iconId, BbAssetCache.Scale) : null;
        }
    }
}
