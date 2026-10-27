using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class PlayerControlSettings : MonoBehaviour
{
    public enum Control
    {
        Forward, Back, Left, Right, Jump, Sprint, Crouch, Slide,
        Dash, Climb, ChangeView, Pickup, Drop, EquipBag, Inventory,
        Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8, Slot9
    }
    public PlayerCameraRig cameraRig;
    public GameObject settingsPanel;
    public bool generateControls = true;
    public KeyCode settingsKey = KeyCode.Escape;
    public Behaviour[] pauseWhileOpen;
    [Range(.1f, 10f)] public float sensitivityX = 2;
    [Range(.1f, 10f)] public float sensitivityY = 2;
    public bool invertY;

    static readonly KeyCode[] defaults = { KeyCode.W,KeyCode.S,KeyCode.A,KeyCode.D,
        KeyCode.Space,KeyCode.LeftShift,KeyCode.LeftControl,KeyCode.C,KeyCode.Q,
        KeyCode.LeftAlt,KeyCode.V,KeyCode.F,KeyCode.G,KeyCode.E,KeyCode.I,
        KeyCode.Alpha1,KeyCode.Alpha2,KeyCode.Alpha3,KeyCode.Alpha4,KeyCode.Alpha5,
        KeyCode.Alpha6,KeyCode.Alpha7,KeyCode.Alpha8,KeyCode.Alpha9 };
    static KeyCode[] keys = (KeyCode[])defaults.Clone();
    static PlayerControlSettings current;
    const string Prefix = "SimplePlayerControls.v1.";
    int capture = -1, captureStarted, ignoreThroughFrame = -1;
    bool opened;
    CursorLockMode previousLock;
    bool previousVisible;
    readonly List<Behaviour> paused = new List<Behaviour>();
    readonly List<Text> keyLabels = new List<Text>();
    Text status;
    Slider xSlider, ySlider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null; keys = (KeyCode[])defaults.Clone(); }
    public static bool GameplayBlocked => current != null &&
        (current.opened || current.capture >= 0 || Time.frameCount <= current.ignoreThroughFrame);
    public static KeyCode Binding(Control control) => keys[(int)control];
    public static bool Pressed(Control control) => !GameplayBlocked && RawKey(keys[(int)control], true);
    public static bool Held(Control control) => !GameplayBlocked && RawKey(keys[(int)control], false);
    public static Vector2 Movement => Vector2.ClampMagnitude(new Vector2(
        (Held(Control.Right) ? 1 : 0) - (Held(Control.Left) ? 1 : 0),
        (Held(Control.Forward) ? 1 : 0) - (Held(Control.Back) ? 1 : 0)), 1);

    void Awake()
    {
        if (current != null && current != this) { Debug.LogError("Use one PlayerControlSettings for the local player.", this); enabled = false; return; }
        current = this;
        for (int i = 0; i < keys.Length; i++)
        {
            var key = (KeyCode)PlayerPrefs.GetInt(Prefix + "Key." + i, (int)defaults[i]);
            keys[i] = Supported(key) && key != settingsKey ? key : defaults[i];
        }
        // Older settings used Space for both Jump and Climb. Migrate that saved
        // duplicate so climbing no longer also queues a jump.
        int jump = (int)Control.Jump, climb = (int)Control.Climb;
        if (keys[jump] == keys[climb]) { keys[climb] = KeyCode.LeftAlt; Save(); }
        sensitivityX = SafeSensitivity(PlayerPrefs.GetFloat(Prefix + "SensitivityX", sensitivityX));
        sensitivityY = SafeSensitivity(PlayerPrefs.GetFloat(Prefix + "SensitivityY", sensitivityY));
        invertY = PlayerPrefs.GetInt(Prefix + "InvertY", invertY ? 1 : 0) != 0;
        ApplyCamera();
    }
    void Start()
    {
        if (!enabled) return;
        if (settingsPanel == null || transform.IsChildOf(settingsPanel.transform))
        { Debug.LogError("Assign a Settings Panel that does NOT contain this SettingsManager.", this); return; }
        if (generateControls) BuildUI();
        settingsPanel.SetActive(false);
    }
    static float SafeSensitivity(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 2 : Mathf.Clamp(v, .1f, 10);
    void ApplyCamera()
    {
        if (cameraRig == null) return;
        cameraRig.SetLookSensitivity(sensitivityX, sensitivityY);
        cameraRig.SetInvertY(invertY);
    }
    public void SetSensitivityX(float value) { sensitivityX = SafeSensitivity(value); ApplyCamera(); Save(); }
    public void SetSensitivityY(float value) { sensitivityY = SafeSensitivity(value); ApplyCamera(); Save(); }
    public void SetInvertY(bool value) { invertY = value; ApplyCamera(); Save(); }
    void Save()
    {
        PlayerPrefs.SetFloat(Prefix + "SensitivityX", sensitivityX);
        PlayerPrefs.SetFloat(Prefix + "SensitivityY", sensitivityY);
        PlayerPrefs.SetInt(Prefix + "InvertY", invertY ? 1 : 0);
        for (int i = 0; i < keys.Length; i++) PlayerPrefs.SetInt(Prefix + "Key." + i, (int)keys[i]);
        PlayerPrefs.Save();
    }
    public void ResetDefaults()
    {
        keys = (KeyCode[])defaults.Clone(); capture = -1;
        sensitivityX = sensitivityY = 2; invertY = false;
        if (xSlider != null) xSlider.value = 2;
        if (ySlider != null) ySlider.value = 2;
        ApplyCamera(); Save(); RefreshLabels();
        if (status != null) status.text = "Defaults restored.";
    }
    public void BeginRebind(int action)
    {
        if (!opened || action < 0 || action >= keys.Length) return;
        capture = action; captureStarted = Time.frameCount;
        if (status != null) status.text = "Press a key or mouse button. Escape cancels.";
        RefreshLabels();
    }
    public bool SetBinding(Control action, KeyCode key)
    {
        if ((int)action < 0 || (int)action >= keys.Length || !Supported(key) || key == settingsKey || key == KeyCode.Escape) return false;
        for (int i = 0; i < keys.Length; i++)
            if (i != (int)action && keys[i] == key) return false;
        keys[(int)action] = key; Save(); RefreshLabels(); return true;
    }
    public void TogglePanel() { SetOpen(!opened); }
    public void ClosePanel() { SetOpen(false); }
    public void SetOpen(bool value)
    {
        if (settingsPanel == null || opened == value || transform.IsChildOf(settingsPanel.transform)) return;
        opened = value; capture = -1; ignoreThroughFrame = Time.frameCount;
        settingsPanel.SetActive(value);
        if (value)
        {
            previousLock = Cursor.lockState; previousVisible = Cursor.visible; paused.Clear();
            if (pauseWhileOpen != null) foreach (var b in pauseWhileOpen)
                if (b != null && b.enabled && b != this && !(b is UnityEngine.EventSystems.EventSystem) &&
                    !(b is UnityEngine.EventSystems.BaseInputModule) && !(b is GraphicRaycaster))
                { paused.Add(b); b.enabled = false; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else
        {
            foreach (var b in paused) if (b != null) b.enabled = true;
            paused.Clear(); Cursor.lockState = previousLock; Cursor.visible = previousVisible; Save();
        }
        RefreshLabels();
    }
    void OnDisable() { if (current == this) SetOpen(false); }
    void OnDestroy() { if (current == this) current = null; }
    void Update()
    {
        if (capture >= 0)
        {
            if (Time.frameCount <= captureStarted) return;
            if (RawKey(KeyCode.Escape, true)) { capture = -1; RefreshLabels(); if (status != null) status.text = "Cancelled."; return; }
            KeyCode key = ReadAnyKey(); if (key == KeyCode.None) return;
            bool changed = SetBinding((Control)capture, key);
            if (status != null) status.text = changed ? "Binding saved." : "Key is reserved or already used. Choose another key.";
            if (changed) { capture = -1; RefreshLabels(); }
            return;
        }
        if (RawKey(settingsKey, true)) TogglePanel();
    }
    static bool Supported(KeyCode key)
    {
        if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse4) return true;
#if ENABLE_INPUT_SYSTEM
        return ToNewKey(key) != Key.None;
#else
        return Enum.IsDefined(typeof(KeyCode),key) && key!=KeyCode.None && (int)key<(int)KeyCode.JoystickButton0;
#endif
    }
    static bool RawKey(KeyCode key, bool down)
    {
#if ENABLE_INPUT_SYSTEM
        if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse4)
        {
            var m = Mouse.current; if (m == null) return false;
            var b = key == KeyCode.Mouse0 ? m.leftButton : key == KeyCode.Mouse1 ? m.rightButton :
                key == KeyCode.Mouse2 ? m.middleButton : key == KeyCode.Mouse3 ? m.backButton : m.forwardButton;
            return down ? b.wasPressedThisFrame : b.isPressed;
        }
        var keyboard = Keyboard.current; var mapped = ToNewKey(key);
        return keyboard != null && mapped != Key.None && (down ? keyboard[mapped].wasPressedThisFrame : keyboard[mapped].isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
        return down?Input.GetKeyDown(key):Input.GetKey(key);
#else
        return false;
#endif
    }
#if ENABLE_INPUT_SYSTEM
    static Key ToNewKey(KeyCode key)
    {
        string n = key.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        n = n.Replace("Keypad", "Numpad").Replace("Control", "Ctrl");
        if (n == "Return") n = "Enter";
        if (n == "LeftCommand" || n == "LeftWindows") n = "LeftMeta";
        if (n == "RightCommand" || n == "RightWindows") n = "RightMeta";
        return Enum.TryParse(n, true, out Key result) ? result : Key.None;
    }
#endif
    static KeyCode ReadAnyKey()
    {
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode))) if (Supported(key) && RawKey(key, true)) return key;
        return KeyCode.None;
    }

    // Optional automatically generated UI. Parent must be a UI panel under Canvas.
    void BuildUI()
    {
        var root = new GameObject("Generated Controls", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        root.transform.SetParent(settingsPanel.transform, false); Stretch((RectTransform)root.transform);
        root.GetComponent<Image>().color = new Color(.06f, .07f, .1f, .98f);
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(root.transform, false); Stretch((RectTransform)viewport.transform);
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var r = (RectTransform)content.transform; r.anchorMin = new Vector2(0, 1); r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, 1); r.offsetMin = r.offsetMax = Vector2.zero;
        var layout = content.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(20, 20, 20, 20); layout.spacing = 8; layout.childControlHeight = true; layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = root.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = r; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        MakeText(content.transform, "Controls & Camera", 28);
        xSlider = MakeSlider(content.transform, "Horizontal sensitivity", sensitivityX, SetSensitivityX);
        ySlider = MakeSlider(content.transform, "Vertical sensitivity", sensitivityY, SetSensitivityY);
        var invert = MakeButton(content.transform, "Invert Y: " + invertY, () => { });
        invert.onClick.AddListener(() => { SetInvertY(!invertY); invert.GetComponentInChildren<Text>().text = "Invert Y: " + invertY; });
        // A toggle isn't required for the generated UI; public SetInvertY also
        // supports a manually authored Unity Toggle.
        for (int i = 0; i < keys.Length; i++)
        {
            int action = i; var button = MakeButton(content.transform, "", () => BeginRebind(action));
            keyLabels.Add(button.GetComponentInChildren<Text>());
        }
        status = MakeText(content.transform, "Click an action to change its key.", 16);
        MakeButton(content.transform, "Reset defaults", () => { ResetDefaults(); invert.GetComponentInChildren<Text>().text = "Invert Y: " + invertY; });
        MakeButton(content.transform, "Close", ClosePanel); RefreshLabels();
    }
    void RefreshLabels()
    {
        for (int i = 0; i < keyLabels.Count; i++) keyLabels[i].text = ((Control)i) + ": " + (capture == i ? "Press a key..." : keys[i].ToString());
    }
    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    static Text MakeText(Transform parent, string text, int size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement)); go.transform.SetParent(parent, false);
        var label = go.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = size; label.text = text; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
        go.GetComponent<LayoutElement>().preferredHeight = 40; return label;
    }
    static Button MakeButton(Transform parent, string title, UnityAction clicked)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement)); go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(.2f, .24f, .3f, 1); go.GetComponent<LayoutElement>().preferredHeight = 42;
        var text = MakeText(go.transform, title, 18); Stretch((RectTransform)text.transform);
        var button = go.GetComponent<Button>(); button.onClick.AddListener(clicked); return button;
    }
    static Slider MakeSlider(Transform parent, string title, float value, UnityAction<float> changed)
    {
        var text = MakeText(parent, title + ": " + value.ToString("0.00"), 18);
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Slider), typeof(LayoutElement)); go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 30; go.GetComponent<Image>().color = new Color(.15f, .18f, .22f, 1);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(go.transform, false);
        var hr = (RectTransform)handle.transform; hr.sizeDelta = new Vector2(20, 30); handle.GetComponent<Image>().color = Color.white;
        var slider = go.GetComponent<Slider>(); slider.handleRect = hr; slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = .1f; slider.maxValue = 10; slider.value = value;
        slider.onValueChanged.AddListener(v => { text.text = title + ": " + v.ToString("0.00"); changed(v); }); return slider;
    }
}
