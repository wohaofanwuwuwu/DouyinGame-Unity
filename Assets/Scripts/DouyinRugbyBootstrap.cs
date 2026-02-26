using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DouyinRugbyBootstrap : MonoBehaviour
{
    private enum Team
    {
        Blue,
        Red
    }

    private class Unit
    {
        public string Name;
        public Team Team;
        public bool IsLocalPlayer;
        public bool IsAi;
        public bool RestrictHalf;
        public Rigidbody Rigidbody;
        public Transform Transform;
        public Vector3 SpawnPosition;
        public Quaternion SpawnRotation;
    }

    private const int TeamSize = 5;
    private const int TotalRounds = 4;

    private readonly List<Unit> _allUnits = new List<Unit>();
    private readonly List<Unit> _blueUnits = new List<Unit>();
    private readonly List<Unit> _redUnits = new List<Unit>();

    private Font _font;
    private Canvas _canvas;

    private GameObject _homePanel;
    private GameObject _roomPanel;
    private GameObject _matchingPanel;
    private Text _matchingText;

    private Image _slot1;
    private Image _slot2;

    private GameObject _hudPanel;
    private Text _roundText;
    private Text _scoreText;
    private Text _statusText;

    private SimpleJoystick _moveJoystick;
    private SimpleJoystick _lookJoystick;
    private Button _jumpButton;
    private Camera _mainCamera;
    private float _cameraYaw;
    private float _cameraPitch = 22f;

    private GameObject _fieldRoot;
    private Transform _ballTransform;
    private Unit _humanPlayer;
    private Unit _ballCarrier;

    private bool _worldBuilt;
    private bool _isGameplayActive;
    private bool _isMatching;
    private bool _jumpQueued;
    private float _lastStealTime;

    private int _currentRound = 1;
    private int _blueScore;
    private int _redScore;

    private readonly Vector2 _fieldXRange = new Vector2(-45f, 45f);
    private readonly Vector2 _fieldZRange = new Vector2(-95f, 95f);
    private const float BlueGoalZ = -90f;
    private const float RedGoalZ = 90f;

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        SetupCameraAndLight();
        BuildUI();
        ShowHomePanel();
    }

    private void FixedUpdate()
    {
        if (!_isGameplayActive)
        {
            return;
        }

        if (_humanPlayer != null)
        {
            MoveHuman(_humanPlayer);
        }

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            if (unit.IsLocalPlayer)
            {
                continue;
            }

            if (unit.IsAi)
            {
                MoveAi(unit);
            }
            else
            {
                HoldRemotePlayer(unit);
            }
        }

        HandleBallFollow();
        HandleBallSteal();
        HandleGoalCheck();
    }

    private void LateUpdate()
    {
        if (!_isGameplayActive || _humanPlayer == null || _mainCamera == null)
        {
            return;
        }

        UpdateThirdPersonCamera();
    }

    private void SetupCameraAndLight()
    {
        Camera main = Camera.main;
        if (main == null)
        {
            GameObject cameraGo = new GameObject("Main Camera");
            main = cameraGo.AddComponent<Camera>();
            cameraGo.tag = "MainCamera";
        }

        _mainCamera = main;
        _mainCamera.transform.position = new Vector3(0f, 8f, -12f);
        _mainCamera.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

        Light directional = FindObjectOfType<Light>();
        if (directional == null)
        {
            GameObject lightGo = new GameObject("Directional Light");
            directional = lightGo.AddComponent<Light>();
            directional.type = LightType.Directional;
        }
        directional.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private void BuildUI()
    {
        EnsureEventSystem();

        GameObject canvasGo = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);

        BuildHomePanel();
        BuildRoomPanel();
        BuildMatchingPanel();
        BuildHudAndControls();
    }

    private void BuildHomePanel()
    {
        _homePanel = CreatePanel("HomePanel", _canvas.transform, new Color(0f, 0f, 0f, 0.35f));

        Button createRoomBtn = CreateButton(_homePanel.transform, "创建房间", new Vector2(0f, 140f), new Vector2(420f, 130f));
        createRoomBtn.onClick.AddListener(OnCreateRoomClick);

        Button practiceBtn = CreateButton(_homePanel.transform, "模拟练习", new Vector2(0f, -20f), new Vector2(420f, 130f));
        practiceBtn.onClick.AddListener(OnPracticeClick);
    }

    private void BuildRoomPanel()
    {
        _roomPanel = CreatePanel("RoomPanel", _canvas.transform, new Color(0f, 0f, 0f, 0.35f));

        GameObject slotsRow = new GameObject("SlotsRow", typeof(RectTransform));
        slotsRow.transform.SetParent(_roomPanel.transform, false);
        RectTransform rowRect = slotsRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.68f);
        rowRect.anchorMax = new Vector2(0.5f, 0.68f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = Vector2.zero;
        rowRect.sizeDelta = new Vector2(700f, 220f);

        HorizontalLayoutGroup hlg = slotsRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 40f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlHeight = false;
        hlg.childControlWidth = false;

        _slot1 = CreateSlot(slotsRow.transform, "玩家1");
        _slot2 = CreateSlot(slotsRow.transform, "等待中");

        Button startMatchBtn = CreateButton(_roomPanel.transform, "开始匹配", new Vector2(0f, 0f), new Vector2(460f, 130f));
        RectTransform startMatchRect = startMatchBtn.GetComponent<RectTransform>();
        SetRect(startMatchRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(460f, 130f));
        startMatchBtn.onClick.AddListener(OnStartMatchClick);

        Button backBtn = CreateButton(_roomPanel.transform, "返回", new Vector2(0f, 0f), new Vector2(240f, 90f));
        RectTransform backRect = backBtn.GetComponent<RectTransform>();
        SetRect(backRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -80f), new Vector2(240f, 90f));
        backBtn.onClick.AddListener(ShowHomePanel);
    }

    private void BuildMatchingPanel()
    {
        _matchingPanel = new GameObject("MatchingPanel", typeof(RectTransform), typeof(Image));
        _matchingPanel.transform.SetParent(_canvas.transform, false);
        Image bg = _matchingPanel.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform rect = _matchingPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.96f);
        rect.anchorMax = new Vector2(0.5f, 0.96f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(520f, 120f);

        _matchingText = CreateText(_matchingPanel.transform, "匹配中 0.0s", 42, TextAnchor.MiddleCenter);
        RectTransform textRect = _matchingText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        _matchingPanel.SetActive(false);
    }

    private void BuildHudAndControls()
    {
        _hudPanel = new GameObject("HudPanel", typeof(RectTransform));
        _hudPanel.transform.SetParent(_canvas.transform, false);
        RectTransform hudRect = _hudPanel.GetComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;

        _roundText = CreateText(_hudPanel.transform, "回合 1/4", 36, TextAnchor.UpperLeft);
        SetRect(_roundText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(260f, 60f));

        _scoreText = CreateText(_hudPanel.transform, "蓝 0 : 0 红", 36, TextAnchor.UpperCenter);
        SetRect(_scoreText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(300f, 60f));

        _statusText = CreateText(_hudPanel.transform, "准备开始", 34, TextAnchor.UpperCenter);
        SetRect(_statusText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(760f, 70f));

        _moveJoystick = CreateJoystick(
            "MoveJoystickRoot",
            _hudPanel.transform,
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(220f, 260f),
            new Vector2(260f, 260f));

        _lookJoystick = CreateJoystick(
            "LookJoystickRoot",
            _hudPanel.transform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-220f, -260f),
            new Vector2(260f, 260f));

        _jumpButton = CreateButton(_hudPanel.transform, "跳跃", new Vector2(0f, 0f), new Vector2(220f, 220f));
        RectTransform jumpRect = _jumpButton.GetComponent<RectTransform>();
        SetRect(jumpRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-170f, 280f), new Vector2(220f, 220f));
        _jumpButton.onClick.AddListener(() => _jumpQueued = true);

        _hudPanel.SetActive(false);
    }

    private void ShowHomePanel()
    {
        _homePanel.SetActive(true);
        _roomPanel.SetActive(false);
        _matchingPanel.SetActive(false);
        _hudPanel.SetActive(false);
        _isMatching = false;
    }

    private void OnCreateRoomClick()
    {
        _homePanel.SetActive(false);
        _roomPanel.SetActive(true);
        _matchingPanel.SetActive(false);

        _slot1.color = new Color(0.22f, 0.55f, 1f, 0.95f);
        _slot2.color = new Color(0.5f, 0.5f, 0.5f, 0.95f);
    }

    private void OnPracticeClick()
    {
        EnterGameplay();
    }

    private void OnStartMatchClick()
    {
        if (_isMatching)
        {
            return;
        }
        StartCoroutine(MatchingRoutine());
    }

    private IEnumerator MatchingRoutine()
    {
        _isMatching = true;
        _matchingPanel.SetActive(true);

        float elapsed = 0f;
        while (elapsed < 5f)
        {
            elapsed += Time.deltaTime;
            _matchingText.text = string.Format("匹配中 {0:0.0}s", elapsed);
            yield return null;
        }

        _isMatching = false;
        EnterGameplay();
    }

    private void EnterGameplay()
    {
        _homePanel.SetActive(false);
        _roomPanel.SetActive(false);
        _matchingPanel.SetActive(false);
        _hudPanel.SetActive(true);

        if (!_worldBuilt)
        {
            BuildWorld();
            _worldBuilt = true;
        }

        _fieldRoot.SetActive(true);
        _currentRound = 1;
        _blueScore = 0;
        _redScore = 0;
        _statusText.text = "蓝方持球，冲向红方达阵区";
        _isGameplayActive = true;
        StartRound();
        ResetCameraAngles();
    }

    private void BuildWorld()
    {
        _fieldRoot = new GameObject("FieldRoot");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(_fieldRoot.transform, false);
        ground.transform.localScale = new Vector3(10f, 1f, 20f);
        ground.GetComponent<Renderer>().material.color = new Color(0.17f, 0.45f, 0.17f);

        GameObject centerLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        centerLine.name = "CenterLine";
        centerLine.transform.SetParent(_fieldRoot.transform, false);
        centerLine.transform.position = new Vector3(0f, 0.03f, 0f);
        centerLine.transform.localScale = new Vector3(90f, 0.06f, 0.45f);
        centerLine.GetComponent<Renderer>().material.color = Color.white;

        CreateGoalZone("BlueGoal", BlueGoalZ, new Color(0.3f, 0.45f, 0.9f, 0.7f));
        CreateGoalZone("RedGoal", RedGoalZ, new Color(0.9f, 0.3f, 0.3f, 0.7f));

        BuildUnits();
        BuildBall();
    }

    private void BuildUnits()
    {
        _allUnits.Clear();
        _blueUnits.Clear();
        _redUnits.Clear();

        _humanPlayer = CreateUnit("BluePlayer", Team.Blue, true, false, new Vector3(-6f, 0.35f, -70f), false);
        _blueUnits.Add(_humanPlayer);

        _blueUnits.Add(CreateUnit("BlueRemote_1", Team.Blue, false, false, new Vector3(6f, 0.35f, -70f), false));
        _blueUnits.Add(CreateUnit("BlueAI_1", Team.Blue, false, true, new Vector3(-20f, 0.35f, -45f), true));
        _blueUnits.Add(CreateUnit("BlueAI_2", Team.Blue, false, true, new Vector3(20f, 0.35f, -45f), true));
        _blueUnits.Add(CreateUnit("BlueAI_3", Team.Blue, false, true, new Vector3(0f, 0.35f, -30f), false));

        _redUnits.Add(CreateUnit("RedRemote_1", Team.Red, false, false, new Vector3(-6f, 0.35f, 70f), false));
        _redUnits.Add(CreateUnit("RedRemote_2", Team.Red, false, false, new Vector3(6f, 0.35f, 70f), false));
        _redUnits.Add(CreateUnit("RedAI_1", Team.Red, false, true, new Vector3(-20f, 0.35f, 45f), true));
        _redUnits.Add(CreateUnit("RedAI_2", Team.Red, false, true, new Vector3(20f, 0.35f, 45f), true));
        _redUnits.Add(CreateUnit("RedAI_3", Team.Red, false, true, new Vector3(0f, 0.35f, 30f), false));

        if (_blueUnits.Count != TeamSize || _redUnits.Count != TeamSize)
        {
            Debug.LogWarning("队伍人数不是 5。请检查生成逻辑。");
        }
    }

    private Unit CreateUnit(string unitName, Team team, bool isLocalPlayer, bool isAi, Vector3 position, bool restrictHalf)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = unitName;
        go.transform.SetParent(_fieldRoot.transform, false);
        go.transform.position = position;
        go.transform.localScale = new Vector3(1.0f, 0.6f, 1.0f);

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.material.color = team == Team.Blue ? new Color(0.2f, 0.45f, 1f) : new Color(0.95f, 0.2f, 0.2f);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.drag = 1.5f;
        rb.angularDrag = 0.05f;

        Unit unit = new Unit
        {
            Name = unitName,
            Team = team,
            IsLocalPlayer = isLocalPlayer,
            IsAi = isAi,
            RestrictHalf = restrictHalf,
            Rigidbody = rb,
            Transform = go.transform,
            SpawnPosition = position,
            SpawnRotation = go.transform.rotation
        };

        if (!isAi && !isLocalPlayer)
        {
            rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        }

        _allUnits.Add(unit);
        return unit;
    }

    private void BuildBall()
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "RugbyBall";
        ball.transform.SetParent(_fieldRoot.transform, false);
        ball.transform.localScale = new Vector3(0.55f, 0.35f, 0.35f);
        ball.GetComponent<Renderer>().material.color = new Color(0.45f, 0.24f, 0.07f);

        Collider ballCollider = ball.GetComponent<Collider>();
        if (ballCollider != null)
        {
            ballCollider.enabled = false;
        }

        _ballTransform = ball.transform;
    }

    private void CreateGoalZone(string zoneName, float z, Color color)
    {
        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        zone.name = zoneName;
        zone.transform.SetParent(_fieldRoot.transform, false);
        zone.transform.position = new Vector3(0f, 0.02f, z);
        zone.transform.localScale = new Vector3(36f, 0.04f, 4f);
        zone.GetComponent<Renderer>().material.color = color;
    }

    private void StartRound()
    {
        _roundText.text = string.Format("回合 {0}/{1}", _currentRound, TotalRounds);
        _scoreText.text = string.Format("蓝 {0} : {1} 红", _blueScore, _redScore);

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            unit.Transform.position = unit.SpawnPosition;
            unit.Transform.rotation = unit.SpawnRotation;
            unit.Rigidbody.velocity = Vector3.zero;
        }

        _ballCarrier = _humanPlayer;
        _lastStealTime = -10f;
        _statusText.text = "蓝方持球，冲向红方达阵区";
        HandleBallFollow();
    }

    private void MoveHuman(Unit human)
    {
        Vector2 input = _moveJoystick != null ? _moveJoystick.Value : Vector2.zero;
        Vector3 desired = new Vector3(input.x, 0f, input.y);
        float speed = 9f;

        Vector3 velocity = human.Rigidbody.velocity;
        Vector3 horizontal = desired.normalized * speed * Mathf.Clamp01(desired.magnitude);
        human.Rigidbody.velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

        if (desired.sqrMagnitude > 0.05f)
        {
            human.Transform.rotation = Quaternion.Slerp(human.Transform.rotation, Quaternion.LookRotation(desired), 0.35f);
        }

        if (_jumpQueued && IsGrounded(human))
        {
            human.Rigidbody.velocity = new Vector3(human.Rigidbody.velocity.x, 7.5f, human.Rigidbody.velocity.z);
            _jumpQueued = false;
        }

        ClampIntoField(human);
    }

    private void HoldRemotePlayer(Unit remote)
    {
        remote.Rigidbody.velocity = new Vector3(0f, remote.Rigidbody.velocity.y, 0f);
    }

    private void MoveAi(Unit ai)
    {
        if (_ballCarrier == null)
        {
            return;
        }

        Vector3 target = _ballCarrier.Transform.position;
        Vector3 toTarget = target - ai.Transform.position;
        toTarget.y = 0f;

        float speed = ai.Team == _ballCarrier.Team ? 6.8f : 7.5f;
        Vector3 desiredDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.zero;

        Vector3 currentVelocity = ai.Rigidbody.velocity;
        Vector3 horizontal = desiredDir * speed;
        ai.Rigidbody.velocity = new Vector3(horizontal.x, currentVelocity.y, horizontal.z);

        if (desiredDir.sqrMagnitude > 0.001f)
        {
            ai.Transform.rotation = Quaternion.Slerp(ai.Transform.rotation, Quaternion.LookRotation(desiredDir), 0.25f);
        }

        if (ai.RestrictHalf)
        {
            Vector3 pos = ai.Transform.position;
            if (ai.Team == Team.Blue)
            {
                pos.z = Mathf.Min(pos.z, -1.5f);
            }
            else
            {
                pos.z = Mathf.Max(pos.z, 1.5f);
            }
            ai.Transform.position = pos;
        }

        ClampIntoField(ai);
    }

    private void ClampIntoField(Unit unit)
    {
        Vector3 p = unit.Transform.position;
        p.x = Mathf.Clamp(p.x, _fieldXRange.x, _fieldXRange.y);
        p.z = Mathf.Clamp(p.z, _fieldZRange.x, _fieldZRange.y);
        unit.Transform.position = p;
    }

    private bool IsGrounded(Unit unit)
    {
        return unit.Transform.position.y <= 0.36f;
    }

    private void HandleBallFollow()
    {
        if (_ballTransform == null || _ballCarrier == null)
        {
            return;
        }

        Vector3 pos = _ballCarrier.Transform.position;
        _ballTransform.position = pos + new Vector3(0f, 0.85f, 0f);
    }

    private void HandleBallSteal()
    {
        if (_ballCarrier == null)
        {
            return;
        }

        if (Time.time - _lastStealTime < 0.35f)
        {
            return;
        }

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            if (unit == _ballCarrier || unit.Team == _ballCarrier.Team)
            {
                continue;
            }

            float dist = Vector3.Distance(unit.Transform.position, _ballCarrier.Transform.position);
            if (dist < 1.25f)
            {
                _ballCarrier = unit;
                _lastStealTime = Time.time;
                _statusText.text = _ballCarrier.Team == Team.Blue ? "蓝方抢到球" : "红方抢到球";
                break;
            }
        }
    }

    private void HandleGoalCheck()
    {
        if (_ballCarrier == null)
        {
            return;
        }

        if (_ballCarrier.Team == Team.Blue && _ballCarrier.Transform.position.z >= RedGoalZ)
        {
            _blueScore += 1;
            StartCoroutine(RoundOverRoutine("蓝方本回合得分"));
        }
        else if (_ballCarrier.Team == Team.Red && _ballCarrier.Transform.position.z <= BlueGoalZ)
        {
            _redScore += 1;
            StartCoroutine(RoundOverRoutine("红方本回合得分"));
        }
    }

    private IEnumerator RoundOverRoutine(string resultText)
    {
        _isGameplayActive = false;
        _statusText.text = resultText;
        _scoreText.text = string.Format("蓝 {0} : {1} 红", _blueScore, _redScore);

        for (int i = 0; i < _allUnits.Count; i++)
        {
            _allUnits[i].Rigidbody.velocity = Vector3.zero;
        }

        yield return new WaitForSeconds(1.8f);

        _currentRound += 1;
        if (_currentRound > TotalRounds)
        {
            string finalText;
            if (_blueScore > _redScore)
            {
                finalText = "蓝方赢得比赛";
            }
            else if (_redScore > _blueScore)
            {
                finalText = "红方赢得比赛";
            }
            else
            {
                finalText = "平局";
            }

            _statusText.text = finalText + "，点击返回首页再开一局";
            _roundText.text = string.Format("回合 {0}/{1}", TotalRounds, TotalRounds);
            _isGameplayActive = false;
            yield break;
        }

        StartRound();
        _isGameplayActive = true;
    }

    private Image CreateSlot(Transform parent, string label)
    {
        GameObject slot = new GameObject(label, typeof(RectTransform), typeof(Image));
        slot.transform.SetParent(parent, false);

        RectTransform rect = slot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(300f, 180f);

        Image img = slot.GetComponent<Image>();
        img.color = new Color(0.5f, 0.5f, 0.5f, 0.95f);

        Text text = CreateText(slot.transform, label, 34, TextAnchor.MiddleCenter);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return img;
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        Image image = panel.GetComponent<Image>();
        image.color = color;

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return panel;
    }

    private Button CreateButton(Transform parent, string text, Vector2 anchoredPos, Vector2 size)
    {
        GameObject btnGo = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        RectTransform rect = btnGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Image image = btnGo.GetComponent<Image>();
        image.color = new Color(0.2f, 0.55f, 1f, 0.95f);

        Text btnText = CreateText(btnGo.transform, text, 44, TextAnchor.MiddleCenter);
        SetRect(btnText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return btnGo.GetComponent<Button>();
    }

    private Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
    {
        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(parent, false);
        Text text = textGo.GetComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.text = content;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
    }

    private static void EnsureEventSystem()
    {
        EventSystem existing = FindObjectOfType<EventSystem>();
        if (existing != null)
        {
            return;
        }

        GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        es.transform.SetParent(null);
    }

    private SimpleJoystick CreateJoystick(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        GameObject joystickRoot = new GameObject(name, typeof(RectTransform));
        joystickRoot.transform.SetParent(parent, false);
        RectTransform joystickRect = joystickRoot.GetComponent<RectTransform>();
        SetRect(joystickRect, anchorMin, anchorMax, anchoredPos, size);

        Image joyBg = joystickRoot.AddComponent<Image>();
        joyBg.color = new Color(1f, 1f, 1f, 0.20f);

        GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(joystickRoot.transform, false);
        RectTransform handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(110f, 110f);
        handleRect.anchoredPosition = Vector2.zero;
        Image handleImage = handleGo.GetComponent<Image>();
        handleImage.color = new Color(1f, 1f, 1f, 0.55f);

        SimpleJoystick joystick = joystickRoot.AddComponent<SimpleJoystick>();
        joystick.Initialize(joystickRect, handleRect, 90f);
        return joystick;
    }

    private void ResetCameraAngles()
    {
        if (_humanPlayer == null || _mainCamera == null)
        {
            return;
        }

        _cameraYaw = _humanPlayer.Transform.eulerAngles.y;
        _cameraPitch = 22f;
        UpdateThirdPersonCamera();
    }

    private void UpdateThirdPersonCamera()
    {
        if (_lookJoystick != null)
        {
            Vector2 look = _lookJoystick.Value;
            _cameraYaw += look.x * 140f * Time.deltaTime;
            _cameraPitch -= look.y * 110f * Time.deltaTime;
            _cameraPitch = Mathf.Clamp(_cameraPitch, 12f, 68f);
        }

        Vector3 target = _humanPlayer.Transform.position + Vector3.up * 1.6f;
        Quaternion orbit = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        Vector3 desiredPos = target - orbit * Vector3.forward * 8.5f;

        _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, desiredPos, 0.18f);
        _mainCamera.transform.rotation = Quaternion.LookRotation(target - _mainCamera.transform.position, Vector3.up);
    }

    private class SimpleJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _background;
        private RectTransform _handle;
        private float _radius;

        public Vector2 Value { get; private set; }

        public void Initialize(RectTransform background, RectTransform handle, float radius)
        {
            _background = background;
            _handle = handle;
            _radius = radius;
            Value = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_background == null || _handle == null)
            {
                return;
            }

            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_background, eventData.position, eventData.pressEventCamera, out localPoint);
            Vector2 clamped = Vector2.ClampMagnitude(localPoint, _radius);
            _handle.anchoredPosition = clamped;
            Value = clamped / _radius;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_handle != null)
            {
                _handle.anchoredPosition = Vector2.zero;
            }
            Value = Vector2.zero;
        }
    }
}
