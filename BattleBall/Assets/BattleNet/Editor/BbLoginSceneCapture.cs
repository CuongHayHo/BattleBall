using System.IO;
using BattleBall.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.EditorTools
{
    /// <summary>
    /// Renders the login scene to PNG files (Logs/login_*.png) in several states and two orientations, to check the layout without playing.
    /// Needs a graphics device: run Unity WITHOUT -nographics. Batch: -executeMethod BattleBall.EditorTools.BbLoginSceneCapture.CaptureBatch
    /// </summary>
    public static class BbLoginSceneCapture
    {
        [MenuItem("BattleBall/Capture Login Screenshots")]
        public static void Capture()
        {
            CheckRules();
            EditorSceneManager.OpenScene(BbLoginSceneBuilder.ScenePath, OpenSceneMode.Single);
            var ui = Object.FindObjectOfType<BbLoginUi>();
            var cam = Camera.main;
            Directory.CreateDirectory("Logs");

            var space = Object.FindObjectOfType<BbSpaceBackground>();
            space.Build();
            space.Apply(25f);

            // loading screen (as the player first sees it)
            ui.barFill.fillAmount = 0.55f;
            Shoot(cam, 1280, 720, "Logs/login_1_loading.png");

            // saved-account menu
            Show(ui, ui.menuRoot);
            ui.resumeLabel.text = "Chơi TK: nguoi_choi1";
            ui.serverLabel.text = "Máy chủ: BattleBall";
            Shoot(cam, 1280, 720, "Logs/login_2_menu.png");
            space.Apply(70f);                                         // 45 s later: stars scrolled, planets drifted and turned
            Shoot(cam, 1280, 720, "Logs/login_2b_menu_later.png");
            space.Apply(25f);

            // login form
            Show(ui, ui.formRoot);
            ui.confirmRow.SetActive(false);
            ui.panel.sizeDelta = new Vector2(620, 230);
            ui.buttonRow.anchoredPosition = new Vector2(0, ui.panel.anchoredPosition.y - 230 - 20);
            ui.status.rectTransform.anchoredPosition = new Vector2(0, -174);
            ui.userField.text = "";
            Shoot(cam, 1280, 720, "Logs/login_3_login.png");

            // register form with an error, landscape and portrait
            ui.confirmRow.SetActive(true);
            ui.secondLabel.text = "Quay lại";
            ui.panel.sizeDelta = new Vector2(620, 306);
            ui.buttonRow.anchoredPosition = new Vector2(0, ui.panel.anchoredPosition.y - 306 - 20);
            ui.status.rectTransform.anchoredPosition = new Vector2(0, -246);
            ui.userField.text = "nguoi_choi1";
            ui.passField.text = "matkhau123";
            ui.confirmField.text = "matkhau12";
            ui.status.text = BbLoginUi.Validate("nguoi_choi1", "matkhau123", "matkhau12", true);
            ui.status.color = new Color(0.72f, 0.12f, 0.08f);
            Shoot(cam, 1280, 720, "Logs/login_4_register.png");
            Shoot(cam, 720, 1280, "Logs/login_5_register_portrait.png");
        }

        static void Show(BbLoginUi ui, GameObject root)
        {
            ui.loadingRoot.SetActive(root == ui.loadingRoot);
            ui.menuRoot.SetActive(root == ui.menuRoot);
            ui.formRoot.SetActive(root == ui.formRoot);
            ui.clearDataButton.gameObject.SetActive(root == ui.loadingRoot || root == ui.menuRoot);
        }

        static void CheckRules()
        {
            void Expect(bool ok, string what) { if (!ok) throw new System.Exception("rule check failed: " + what); }
            Expect(BbLoginUi.Validate("abcd", "password1", "", false) == null, "valid login");
            Expect(BbLoginUi.Validate("abc", "password1", "", false) != null, "name too short");
            Expect(BbLoginUi.Validate("a_b_c_d_e_f_g_h_i_j_k", "password1", "", false) != null, "name too long");
            Expect(BbLoginUi.Validate("Abcd", "password1", "", false) != null, "uppercase name");
            Expect(BbLoginUi.Validate("abcd", "short", "", false) != null, "password too short");
            Expect(BbLoginUi.Validate("abcd", new string('x', 65), "", false) != null, "password too long");
            Expect(BbLoginUi.Validate("abcd", "password1", "password2", true) != null, "passwords differ");
            Expect(BbLoginUi.Validate("abcd", "password1", "password1", true) == null, "valid register");
            Debug.Log("login rule checks passed");
        }

        public static void CaptureBatch()
        {
            try { Capture(); }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        internal static void Shoot(Camera cam, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
