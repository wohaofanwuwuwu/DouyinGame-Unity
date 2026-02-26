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
        public Collider Collider;
        public Renderer Renderer;
        public Vector3 SpawnPosition;
        public Quaternion SpawnRotation;
        public float MaxHealth;
        public float Health;
        public bool IsDead;
        public float NextAttackAllowedTime;
        public RectTransform HpBarRoot;
        public Image HpBarFill;
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
    private Text _roundTimerText;
    private Text _hpText;

    private SimpleJoystick _moveJoystick;
    private SimpleJoystick _lookJoystick;
    private Button _jumpButton;
    private Button _actionButton;
    private Text _actionButtonText;
    private RectTransform _cancelThrowRect;
    private Text _cancelThrowText;
    private Camera _mainCamera;
    private float _cameraYaw;
    private float _cameraPitch = 22f;

    private GameObject _fieldRoot;
    private Transform _ballTransform;
    private Rigidbody _ballRigidbody;
    private Collider _ballCollider;
    private Unit _humanPlayer;
    private Unit _ballCarrier;
    private bool _ballIsLoose;
    private float _ballPickupLockedUntil;
    private bool _isChargingThrow;
    private float _throwHoldTime;
    private bool _cancelThrowOnRelease;
    private bool _isRoundEnding;
    private float _roundElapsed;
    private float _aiAttackTickTimer;

    private GameObject _cardPanel;
    private Text _cardCountdownText;
    private Text _cardANameText;
    private Text _cardBNameText;
    private string _cardAName;
    private string _cardBName;
    private bool _cardChosen;
    private bool _cardPhaseActive;
    private int _cardSelectionsDone;

    private static readonly string[] CardPool = new string[]
    {
        "冲刺强化",
        "跳跃增强",
        "护盾启动",
        "体力恢复",
        "冲撞提升",
        "减伤护甲",
        "爆发加速",
        "抢断专精",
        "传球强化",
        "视野拓展",
        "防守站位",
        "反击突进",
        "队友鼓舞",
        "恢复光环",
        "冲锋准备",
        "持球稳固"
    };

    private bool _worldBuilt;
    private bool _isGameplayActive;
    private bool _isMatching;
    private bool _jumpQueued;

    private int _currentRound = 1;
    private int _blueScore;
    private int _redScore;

    private readonly Vector2 _fieldXRange = new Vector2(-65f, 65f);
    private readonly Vector2 _fieldZRange = new Vector2(-130f, 130f);
    private const float BlueGoalZ = -124f;
    private const float RedGoalZ = 124f;
    private const float GoalHalfDepth = 3f;
    private const float AttackCooldown = 1f;
    private const float AttackRange = 3.2f;
    private const float AttackHalfAngle = 42f;
    private const float MaxThrowCharge = 1.8f;
    private const float BallPickupRadius = 2.1f;

    private readonly List<Transform> _trajectoryPoints = new List<Transform>();
    private Transform _trajectoryLandingMarker;

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

        HandleAiAutoAttack();
        HandleBallFollow();
        HandleLooseBallPickup();
        HandleGoalCheck();
        UpdateHpText();
    }

    private void LateUpdate()
    {
        if (!_isGameplayActive || _humanPlayer == null || _mainCamera == null)
        {
            return;
        }

        UpdateThirdPersonCamera();
        UpdateWorldHpBars();
    }

    private void Update()
    {
        if (_isGameplayActive && !_isRoundEnding)
        {
            _roundElapsed += Time.deltaTime;
            _roundTimerText.text = "计时 " + FormatTime(_roundElapsed);

            if (_isChargingThrow && _humanPlayer != null && _ballCarrier == _humanPlayer)
            {
                _throwHoldTime += Time.deltaTime;
                UpdateThrowPreview();
            }
        }

        // 移动端常见情况：手指离开时不一定在按钮上，补一个全局松手检测。
        if (_isChargingThrow)
        {
            if (Input.GetMouseButtonUp(0))
            {
                HandleThrowRelease(Input.mousePosition, null);
            }
            else if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    {
                        HandleThrowRelease(t.position, null);
                        break;
                    }
                }
            }
        }
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

        Button createRoomBtn = CreateButton(_homePanel.transform, "创建房间", new Vector2(0f, 110f), new Vector2(320f, 96f));
        createRoomBtn.onClick.AddListener(OnCreateRoomClick);

        Button practiceBtn = CreateButton(_homePanel.transform, "模拟练习", new Vector2(0f, -12f), new Vector2(320f, 96f));
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
        rowRect.sizeDelta = new Vector2(560f, 170f);

        HorizontalLayoutGroup hlg = slotsRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 28f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlHeight = false;
        hlg.childControlWidth = false;

        _slot1 = CreateSlot(slotsRow.transform, "玩家1");
        _slot2 = CreateSlot(slotsRow.transform, "等待中");

        Button startMatchBtn = CreateButton(_roomPanel.transform, "开始匹配", new Vector2(0f, 0f), new Vector2(340f, 96f));
        RectTransform startMatchRect = startMatchBtn.GetComponent<RectTransform>();
        SetRect(startMatchRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(340f, 96f));
        startMatchBtn.onClick.AddListener(OnStartMatchClick);

        Button backBtn = CreateButton(_roomPanel.transform, "返回", new Vector2(0f, 0f), new Vector2(190f, 72f));
        RectTransform backRect = backBtn.GetComponent<RectTransform>();
        SetRect(backRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -64f), new Vector2(190f, 72f));
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
        rect.sizeDelta = new Vector2(390f, 86f);

        _matchingText = CreateText(_matchingPanel.transform, "匹配中 0.0s", 30, TextAnchor.MiddleCenter);
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

        _roundText = CreateText(_hudPanel.transform, "回合 1/4", 16, TextAnchor.UpperLeft);
        SetRect(_roundText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(150f, 34f));

        _scoreText = CreateText(_hudPanel.transform, "蓝 0 : 0 红", 16, TextAnchor.UpperCenter);
        SetRect(_scoreText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(160f, 34f));

        _statusText = CreateText(_hudPanel.transform, "准备开始", 14, TextAnchor.UpperCenter);
        SetRect(_statusText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(420f, 34f));

        _roundTimerText = CreateText(_hudPanel.transform, "计时 00:00", 14, TextAnchor.UpperRight);
        SetRect(_roundTimerText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-58f, -24f), new Vector2(150f, 34f));

        _hpText = CreateText(_hudPanel.transform, "HP 100", 14, TextAnchor.UpperLeft);
        SetRect(_hpText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -52f), new Vector2(150f, 34f));

        _moveJoystick = CreateJoystick(
            "MoveJoystickRoot",
            _hudPanel.transform,
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(122f, 136f),
            new Vector2(140f, 140f));

        _lookJoystick = CreateJoystick(
            "LookJoystickRoot",
            _hudPanel.transform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-122f, -136f),
            new Vector2(140f, 140f));

        _jumpButton = CreateButton(_hudPanel.transform, "跳跃", new Vector2(0f, 0f), new Vector2(104f, 104f));
        RectTransform jumpRect = _jumpButton.GetComponent<RectTransform>();
        SetRect(jumpRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-88f, 136f), new Vector2(104f, 104f));
        _jumpButton.onClick.AddListener(() => _jumpQueued = true);

        _actionButton = CreateButton(_hudPanel.transform, "攻击", new Vector2(0f, 0f), new Vector2(104f, 104f));
        RectTransform actionRect = _actionButton.GetComponent<RectTransform>();
        SetRect(actionRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-188f, 136f), new Vector2(104f, 104f));
        _actionButtonText = _actionButton.GetComponentInChildren<Text>();
        AddActionButtonEvents(_actionButton.gameObject);

        GameObject cancelZone = new GameObject("CancelThrowZone", typeof(RectTransform), typeof(Image));
        cancelZone.transform.SetParent(_hudPanel.transform, false);
        _cancelThrowRect = cancelZone.GetComponent<RectTransform>();
        SetRect(_cancelThrowRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-188f, 254f), new Vector2(72f, 72f));
        Image cancelBg = cancelZone.GetComponent<Image>();
        cancelBg.color = new Color(0.95f, 0.2f, 0.2f, 0.85f);
        _cancelThrowText = CreateText(cancelZone.transform, "X", 28, TextAnchor.MiddleCenter);
        SetRect(_cancelThrowText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cancelZone.SetActive(false);

        BuildThrowPreview();
        BuildCardPanel();
        _hudPanel.SetActive(false);
    }

    private void ShowHomePanel()
    {
        _homePanel.SetActive(true);
        _roomPanel.SetActive(false);
        _matchingPanel.SetActive(false);
        _hudPanel.SetActive(false);
        _isMatching = false;
        HideThrowPreview();
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
        _isRoundEnding = false;
        _statusText.text = "蓝方持球，冲向红方达阵区";
        StartCoroutine(BeginRoundRoutine());
        ResetCameraAngles();
    }

    private void BuildWorld()
    {
        _fieldRoot = new GameObject("FieldRoot");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(_fieldRoot.transform, false);
        ground.transform.localScale = new Vector3(14f, 1f, 28f);
        ground.GetComponent<Renderer>().material.color = new Color(0.17f, 0.45f, 0.17f);

        GameObject centerLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        centerLine.name = "CenterLine";
        centerLine.transform.SetParent(_fieldRoot.transform, false);
        centerLine.transform.position = new Vector3(0f, 0.03f, 0f);
        centerLine.transform.localScale = new Vector3(130f, 0.06f, 0.5f);
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

        _humanPlayer = CreateUnit("BluePlayer", Team.Blue, true, false, new Vector3(-10f, 1.0f, -95f), false);
        _blueUnits.Add(_humanPlayer);

        _blueUnits.Add(CreateUnit("BlueRemote_1", Team.Blue, false, false, new Vector3(10f, 1.0f, -95f), false));
        _blueUnits.Add(CreateUnit("BlueAI_1", Team.Blue, false, true, new Vector3(-28f, 1.0f, -66f), true));
        _blueUnits.Add(CreateUnit("BlueAI_2", Team.Blue, false, true, new Vector3(28f, 1.0f, -66f), true));
        _blueUnits.Add(CreateUnit("BlueAI_3", Team.Blue, false, true, new Vector3(0f, 1.0f, -44f), false));

        _redUnits.Add(CreateUnit("RedRemote_1", Team.Red, false, false, new Vector3(-10f, 1.0f, 95f), false));
        _redUnits.Add(CreateUnit("RedRemote_2", Team.Red, false, false, new Vector3(10f, 1.0f, 95f), false));
        _redUnits.Add(CreateUnit("RedAI_1", Team.Red, false, true, new Vector3(-28f, 1.0f, 66f), true));
        _redUnits.Add(CreateUnit("RedAI_2", Team.Red, false, true, new Vector3(28f, 1.0f, 66f), true));
        _redUnits.Add(CreateUnit("RedAI_3", Team.Red, false, true, new Vector3(0f, 1.0f, 44f), false));

        if (_blueUnits.Count != TeamSize || _redUnits.Count != TeamSize)
        {
            Debug.LogWarning("队伍人数不是 5。请检查生成逻辑。");
        }
    }

    private Unit CreateUnit(string unitName, Team team, bool isLocalPlayer, bool isAi, Vector3 position, bool restrictHalf)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = unitName;
        go.transform.SetParent(_fieldRoot.transform, false);
        go.transform.position = position;
        go.transform.localScale = new Vector3(1.25f, 1.15f, 1.25f);

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.material.color = team == Team.Blue ? new Color(0.2f, 0.45f, 1f) : new Color(0.95f, 0.2f, 0.2f);
        Collider collider = go.GetComponent<Collider>();

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
            Collider = collider,
            Renderer = renderer,
            SpawnPosition = position,
            SpawnRotation = go.transform.rotation,
            MaxHealth = 10f,
            Health = 10f,
            IsDead = false
        };

        CreateUnitHpBar(unit);

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
        ball.transform.localScale = new Vector3(0.52f, 0.38f, 0.38f);
        ball.GetComponent<Renderer>().material.color = new Color(0.45f, 0.24f, 0.07f);

        _ballCollider = ball.GetComponent<Collider>();
        if (_ballCollider != null)
        {
            PhysicMaterial ballMat = new PhysicMaterial("BallPhysicsMaterial");
            ballMat.dynamicFriction = 0.65f;
            ballMat.staticFriction = 0.55f;
            ballMat.bounciness = 0.08f;
            ballMat.frictionCombine = PhysicMaterialCombine.Average;
            ballMat.bounceCombine = PhysicMaterialCombine.Minimum;
            _ballCollider.material = ballMat;
        }

        _ballTransform = ball.transform;
        _ballRigidbody = ball.AddComponent<Rigidbody>();
        _ballRigidbody.mass = 0.6f;
        _ballRigidbody.drag = 0.2f;
        _ballRigidbody.angularDrag = 0.12f;
        _ballRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _ballRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        SetBallAttachedToCarrier(_humanPlayer);
    }

    private void CreateGoalZone(string zoneName, float z, Color color)
    {
        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        zone.name = zoneName;
        zone.transform.SetParent(_fieldRoot.transform, false);
        zone.transform.position = new Vector3(0f, 0.02f, z);
        zone.transform.localScale = new Vector3(130f, 0.04f, 6f);
        zone.GetComponent<Renderer>().material.color = color;
    }

    private void StartRound()
    {
        _roundText.text = string.Format("回合 {0}/{1}", _currentRound, TotalRounds);
        _scoreText.text = string.Format("蓝 {0} : {1} 红", _blueScore, _redScore);
        _roundElapsed = 0f;
        _roundTimerText.text = "计时 00:00";
        _aiAttackTickTimer = 0f;
        _isRoundEnding = false;
        _isChargingThrow = false;
        _throwHoldTime = 0f;
        _cancelThrowOnRelease = false;
        if (_cancelThrowRect != null)
        {
            _cancelThrowRect.gameObject.SetActive(false);
        }
        HideThrowPreview();

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            unit.Transform.position = unit.SpawnPosition;
            unit.Transform.rotation = unit.SpawnRotation;
            unit.Rigidbody.velocity = Vector3.zero;
            unit.Health = unit.MaxHealth;
            unit.IsDead = false;
            unit.NextAttackAllowedTime = 0f;
            if (unit.Collider != null)
            {
                unit.Collider.enabled = true;
            }
            if (unit.Renderer != null)
            {
                Color c = unit.Renderer.material.color;
                c.a = 1f;
                unit.Renderer.material.color = c;
            }
            ApplyRoleConstraints(unit);
        }

        SetBallAttachedToCarrier(_humanPlayer);
        _statusText.text = "蓝方持球，冲向红方达阵区";
        HandleBallFollow();
        UpdateHpText();
        UpdateActionButtonLabel();
    }

    private void MoveHuman(Unit human)
    {
        if (human.IsDead)
        {
            return;
        }

        Vector2 input = _moveJoystick != null ? _moveJoystick.Value : Vector2.zero;
        Vector3 camForward = Vector3.forward;
        Vector3 camRight = Vector3.right;
        if (_mainCamera != null)
        {
            camForward = _mainCamera.transform.forward;
            camRight = _mainCamera.transform.right;
        }
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        // 使用相机相对移动，修正左右后方向与视角不一致的问题。
        Vector3 desired = camRight * input.x + camForward * input.y;
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
        if (remote.IsDead)
        {
            return;
        }

        remote.Rigidbody.velocity = new Vector3(0f, remote.Rigidbody.velocity.y, 0f);
    }

    private void MoveAi(Unit ai)
    {
        if (ai.IsDead)
        {
            return;
        }

        if (_ballCarrier == null && !_ballIsLoose)
        {
            return;
        }

        Vector3 target;
        float speed;

        if (_ballIsLoose && _ballCarrier == null && _ballTransform != null)
        {
            target = _ballTransform.position;
            speed = 8.2f;
        }
        else if (ai == _ballCarrier)
        {
            float goalZ = ai.Team == Team.Blue ? RedGoalZ : BlueGoalZ;
            target = new Vector3(0f, ai.Transform.position.y, goalZ);
            speed = 7.9f;
        }
        else if (_ballCarrier != null && ai.Team == _ballCarrier.Team)
        {
            Unit nearestEnemy = FindNearestAliveEnemy(ai, ai.Team == Team.Blue ? Team.Red : Team.Blue);
            target = nearestEnemy != null ? nearestEnemy.Transform.position : _ballCarrier.Transform.position;
            speed = 6.9f;
        }
        else
        {
            if (_ballCarrier == null)
            {
                return;
            }
            target = _ballCarrier.Transform.position;
            speed = 7.6f;
        }

        Vector3 toTarget = target - ai.Transform.position;
        toTarget.y = 0f;
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
        return unit.Transform.position.y <= 1.08f;
    }

    private void HandleBallFollow()
    {
        if (_ballTransform == null || _ballCarrier == null || _ballIsLoose)
        {
            return;
        }

        Vector3 pos = _ballCarrier.Transform.position;
        _ballTransform.position = pos + new Vector3(0f, 1.65f, 0f);
        _ballTransform.rotation = Quaternion.identity;
    }

    private void HandleLooseBallPickup()
    {
        if (!_ballIsLoose || _ballCarrier != null || _isRoundEnding)
        {
            return;
        }

        if (Time.time < _ballPickupLockedUntil)
        {
            return;
        }

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            if (unit.IsDead)
            {
                continue;
            }

            float dist = Vector3.Distance(unit.Transform.position, _ballTransform.position);
            if (dist <= BallPickupRadius)
            {
                SetBallAttachedToCarrier(unit);
                _statusText.text = unit.Name + " 捡到了球";
                if (unit.IsAi)
                {
                    TryAiImmediatePass(unit);
                }
                break;
            }
        }
    }

    private void HandleGoalCheck()
    {
        if (_ballCarrier == null || _ballCarrier.IsDead || _isRoundEnding)
        {
            return;
        }

        float carrierZ = _ballCarrier.Transform.position.z;

        if (_ballCarrier.Team == Team.Blue && carrierZ >= RedGoalZ - GoalHalfDepth)
        {
            _blueScore += 1;
            StartCoroutine(RoundOverRoutine("蓝方本回合得分"));
        }
        else if (_ballCarrier.Team == Team.Red && carrierZ <= BlueGoalZ + GoalHalfDepth)
        {
            _redScore += 1;
            StartCoroutine(RoundOverRoutine("红方本回合得分"));
        }
    }

    private IEnumerator RoundOverRoutine(string resultText)
    {
        if (_isRoundEnding)
        {
            yield break;
        }

        _isRoundEnding = true;
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

        StartCoroutine(BeginRoundRoutine());
    }

    private Image CreateSlot(Transform parent, string label)
    {
        GameObject slot = new GameObject(label, typeof(RectTransform), typeof(Image));
        slot.transform.SetParent(parent, false);

        RectTransform rect = slot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(230f, 130f);

        Image img = slot.GetComponent<Image>();
        img.color = new Color(0.5f, 0.5f, 0.5f, 0.95f);

        Text text = CreateText(slot.transform, label, 24, TextAnchor.MiddleCenter);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return img;
    }

    private void BuildCardPanel()
    {
        _cardPanel = new GameObject("CardPanel", typeof(RectTransform), typeof(Image));
        _cardPanel.transform.SetParent(_hudPanel.transform, false);

        Image panelBg = _cardPanel.GetComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.78f);

        RectTransform panelRect = _cardPanel.GetComponent<RectTransform>();
        SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 360f));

        Text title = CreateText(_cardPanel.transform, "第2回合起可选卡牌", 22, TextAnchor.UpperCenter);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(500f, 40f));

        _cardCountdownText = CreateText(_cardPanel.transform, "剩余 10s", 18, TextAnchor.UpperCenter);
        SetRect(_cardCountdownText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(260f, 34f));

        Button cardAButton = CreateButton(_cardPanel.transform, "卡牌A", Vector2.zero, new Vector2(220f, 90f));
        RectTransform cardARect = cardAButton.GetComponent<RectTransform>();
        SetRect(cardARect, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f), new Vector2(-132f, 0f), new Vector2(220f, 90f));
        _cardANameText = cardAButton.GetComponentInChildren<Text>();
        cardAButton.onClick.AddListener(() => OnCardSelected(0));

        Button cardBButton = CreateButton(_cardPanel.transform, "卡牌B", Vector2.zero, new Vector2(220f, 90f));
        RectTransform cardBRect = cardBButton.GetComponent<RectTransform>();
        SetRect(cardBRect, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f), new Vector2(132f, 0f), new Vector2(220f, 90f));
        _cardBNameText = cardBButton.GetComponentInChildren<Text>();
        cardBButton.onClick.AddListener(() => OnCardSelected(1));

        Text hint = CreateText(_cardPanel.transform, "10秒后自动选左卡；或全员选完提前结束", 15, TextAnchor.LowerCenter);
        SetRect(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(500f, 28f));

        _cardPanel.SetActive(false);
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

        Text btnText = CreateText(btnGo.transform, text, 20, TextAnchor.MiddleCenter);
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

    private static Sprite _circleSpriteCache;

    private static Sprite GetCircleSprite()
    {
        if (_circleSpriteCache != null)
            return _circleSpriteCache;
        const int size = 128;
        float radius = size * 0.5f;
        float center = radius;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        float rOuter = radius;
        float rInner = radius - 1.2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = dist <= rInner ? 1f : (dist <= rOuter ? Mathf.Clamp01(1f - (dist - rInner) / 1.2f) : 0f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        _circleSpriteCache = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _circleSpriteCache;
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

    private IEnumerator BeginRoundRoutine()
    {
        StartRound();
        ResetCameraAngles();

        if (_currentRound >= 2)
        {
            _isGameplayActive = false;
            _cardChosen = false;
            _cardPhaseActive = true;
            _cardSelectionsDone = 0;

            PickTwoRandomCards(out _cardAName, out _cardBName);
            _cardANameText.text = _cardAName;
            _cardBNameText.text = _cardBName;
            _cardCountdownText.text = "剩余 10.0s";
            _cardPanel.SetActive(true);
            _statusText.text = "暂停：选择本回合卡牌";
            StartCoroutine(SimulateOtherPlayerCardPicks());

            float remain = 10f;
            while (remain > 0f && _cardSelectionsDone < _allUnits.Count)
            {
                _cardCountdownText.text = string.Format("剩余 {0:0.0}s  已选 {1}/{2}", remain, _cardSelectionsDone, _allUnits.Count);
                remain -= Time.deltaTime;
                yield return null;
            }

            if (!_cardChosen)
            {
                OnCardSelected(0);
            }

            _cardPhaseActive = false;
            _cardPanel.SetActive(false);
        }

        _statusText.text = "回合开始，蓝方持球推进";
        _isGameplayActive = true;
    }

    private void PickTwoRandomCards(out string a, out string b)
    {
        int first = Random.Range(0, CardPool.Length);
        int second = Random.Range(0, CardPool.Length - 1);
        if (second >= first)
        {
            second += 1;
        }

        a = CardPool[first];
        b = CardPool[second];
    }

    private void OnCardSelected(int index)
    {
        if (_cardChosen)
        {
            return;
        }

        _cardChosen = true;
        _cardSelectionsDone = Mathf.Min(_allUnits.Count, _cardSelectionsDone + 1);
        string selected = index == 0 ? _cardAName : _cardBName;
        _statusText.text = "本回合卡牌：" + selected + "（功能占位）";
    }

    private IEnumerator SimulateOtherPlayerCardPicks()
    {
        int targetOthers = Mathf.Max(0, _allUnits.Count - 1);
        int pickedOthers = 0;

        while (_cardPhaseActive && pickedOthers < targetOthers)
        {
            float wait = Random.Range(0.5f, 1.6f);
            yield return new WaitForSeconds(wait);
            if (!_cardPhaseActive)
            {
                yield break;
            }

            pickedOthers += 1;
            _cardSelectionsDone = Mathf.Min(_allUnits.Count, _cardSelectionsDone + 1);
        }
    }

    private static string FormatTime(float seconds)
    {
        int sec = Mathf.FloorToInt(seconds);
        int min = sec / 60;
        int left = sec % 60;
        return string.Format("{0:00}:{1:00}", min, left);
    }

    private void UpdateHpText()
    {
        if (_hpText == null || _humanPlayer == null)
        {
            return;
        }

        _hpText.text = string.Format("HP {0:0}/{1:0}", Mathf.Max(0f, _humanPlayer.Health), _humanPlayer.MaxHealth);
    }

    private void HandleAiAutoAttack()
    {
        _aiAttackTickTimer += Time.fixedDeltaTime;
        if (_aiAttackTickTimer < 0.1f)
        {
            return;
        }

        _aiAttackTickTimer = 0f;
        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            if (!unit.IsAi || unit.IsDead)
            {
                continue;
            }
            TryAttack(unit);
        }
    }

    private void CreateUnitHpBar(Unit unit)
    {
        if (_hudPanel == null || unit == null)
        {
            return;
        }

        GameObject barRoot = new GameObject(unit.Name + "_HpBar", typeof(RectTransform));
        barRoot.transform.SetParent(_hudPanel.transform, false);
        RectTransform rootRect = barRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(66f, 9f);

        GameObject bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(barRoot.transform, false);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = new Vector2(1f, 1f);
        fillRect.offsetMax = new Vector2(-1f, -1f);
        Image fillImg = fill.GetComponent<Image>();
        fillImg.color = unit.Team == Team.Blue ? new Color(0.2f, 0.72f, 1f, 0.95f) : new Color(1f, 0.25f, 0.25f, 0.95f);

        unit.HpBarRoot = rootRect;
        unit.HpBarFill = fillImg;
    }

    private void UpdateWorldHpBars()
    {
        if (_canvas == null || _mainCamera == null)
        {
            return;
        }

        RectTransform canvasRect = _canvas.GetComponent<RectTransform>();

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit unit = _allUnits[i];
            if (unit.HpBarRoot == null || unit.HpBarFill == null)
            {
                continue;
            }

            if (!_isGameplayActive && !_isRoundEnding)
            {
                unit.HpBarRoot.gameObject.SetActive(false);
                continue;
            }

            Vector3 world = unit.Transform.position + Vector3.up * 3.1f;
            Vector3 screen = _mainCamera.WorldToScreenPoint(world);

            bool visible = screen.z > 0f;
            unit.HpBarRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out localPoint);
            unit.HpBarRoot.anchoredPosition = localPoint;

            float ratio = unit.MaxHealth > 0f ? Mathf.Clamp01(unit.Health / unit.MaxHealth) : 0f;
            RectTransform fillRect = unit.HpBarFill.rectTransform;
            fillRect.anchorMax = new Vector2(ratio, 1f);
        }
    }

    private Unit FindNearestAliveEnemy(Unit from, Team enemyTeam)
    {
        Unit best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit candidate = _allUnits[i];
            if (candidate.IsDead || candidate.Team != enemyTeam)
            {
                continue;
            }

            float d = Vector3.SqrMagnitude(candidate.Transform.position - from.Transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = candidate;
            }
        }

        return best;
    }

    private Unit FindNearestAliveNonAiTeammate(Unit from)
    {
        Unit best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit candidate = _allUnits[i];
            if (candidate == from || candidate.IsDead || candidate.Team != from.Team || candidate.IsAi)
            {
                continue;
            }

            float d = Vector3.SqrMagnitude(candidate.Transform.position - from.Transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = candidate;
            }
        }

        return best;
    }

    private void TryAiImmediatePass(Unit aiCarrier)
    {
        if (aiCarrier == null || !aiCarrier.IsAi || _ballCarrier != aiCarrier || _ballTransform == null)
        {
            return;
        }

        Unit teammate = FindNearestAliveNonAiTeammate(aiCarrier);
        if (teammate == null)
        {
            return;
        }

        Vector3 toTeammate = teammate.Transform.position - aiCarrier.Transform.position;
        float flatDistance = new Vector2(toTeammate.x, toTeammate.z).magnitude;
        Vector3 dir = toTeammate.normalized;
        float passSpeed = Mathf.Clamp(12f + flatDistance * 0.62f, 14f, 34f);
        float arc = Mathf.Clamp(1.4f + flatDistance * 0.032f, 1.8f, 5.6f);

        Vector3 startPos = aiCarrier.Transform.position + aiCarrier.Transform.forward * 0.9f + Vector3.up * 1.55f;
        Vector3 initialVelocity = dir * passSpeed + Vector3.up * arc;

        _ballCarrier = null;
        _ballIsLoose = true;
        _ballPickupLockedUntil = Time.time + 0.55f;
        _ballTransform.position = startPos;
        if (_ballRigidbody != null)
        {
            _ballRigidbody.isKinematic = false;
            _ballRigidbody.detectCollisions = true;
            _ballRigidbody.velocity = initialVelocity;
            _ballRigidbody.angularVelocity = new Vector3(0f, 8f, 0f);
        }
        if (_ballCollider != null)
        {
            _ballCollider.enabled = true;
        }

        _statusText.text = aiCarrier.Name + " 传球给 " + teammate.Name;
        UpdateActionButtonLabel();
    }

    private void AddActionButtonEvents(GameObject buttonGo)
    {
        EventTrigger trigger = buttonGo.AddComponent<EventTrigger>();

        EventTrigger.Entry downEntry = new EventTrigger.Entry();
        downEntry.eventID = EventTriggerType.PointerDown;
        downEntry.callback.AddListener((data) => OnActionButtonPointerDown((PointerEventData)data));
        trigger.triggers.Add(downEntry);

        EventTrigger.Entry upEntry = new EventTrigger.Entry();
        upEntry.eventID = EventTriggerType.PointerUp;
        upEntry.callback.AddListener((data) => OnActionButtonPointerUp((PointerEventData)data));
        trigger.triggers.Add(upEntry);
    }

    private void OnActionButtonPointerDown(PointerEventData eventData)
    {
        if (!_isGameplayActive || _humanPlayer == null || _humanPlayer.IsDead)
        {
            return;
        }

        if (_ballCarrier == _humanPlayer)
        {
            _isChargingThrow = true;
            _throwHoldTime = 0f;
            _cancelThrowOnRelease = false;
            if (_cancelThrowRect != null)
            {
                _cancelThrowRect.gameObject.SetActive(true);
                SetCancelVisual(false);
            }
            UpdateThrowPreview();
        }
    }

    private void OnActionButtonPointerUp(PointerEventData eventData)
    {
        if (_humanPlayer == null || _humanPlayer.IsDead)
        {
            return;
        }

        if (_ballCarrier == _humanPlayer && _isChargingThrow)
        {
            HandleThrowRelease(eventData.position, eventData.pressEventCamera);
            return;
        }

        if (_ballCarrier != _humanPlayer)
        {
            TryAttack(_humanPlayer);
        }
    }

    private void UpdateActionButtonLabel()
    {
        if (_actionButtonText == null || _humanPlayer == null)
        {
            return;
        }

        _actionButtonText.text = _ballCarrier == _humanPlayer ? "投掷" : "攻击";
    }

    private bool TryAttack(Unit attacker)
    {
        if (attacker == null || attacker.IsDead || Time.time < attacker.NextAttackAllowedTime)
        {
            return false;
        }

        Unit target = FindAttackTarget(attacker);
        attacker.NextAttackAllowedTime = Time.time + AttackCooldown;
        if (target == null)
        {
            return false;
        }

        ApplyDamage(target, 1f, attacker);
        return true;
    }

    private Unit FindAttackTarget(Unit attacker)
    {
        Unit best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _allUnits.Count; i++)
        {
            Unit candidate = _allUnits[i];
            if (candidate.IsDead || candidate.Team == attacker.Team)
            {
                continue;
            }

            Vector3 toTarget = candidate.Transform.position - attacker.Transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance > AttackRange || distance <= 0.0001f)
            {
                continue;
            }

            float angle = Vector3.Angle(attacker.Transform.forward, toTarget.normalized);
            if (angle > AttackHalfAngle)
            {
                continue;
            }

            if (distance < bestDist)
            {
                bestDist = distance;
                best = candidate;
            }
        }

        return best;
    }

    private void SetBallAttachedToCarrier(Unit carrier)
    {
        _ballCarrier = carrier;
        _ballIsLoose = false;
        if (_ballRigidbody != null)
        {
            _ballRigidbody.velocity = Vector3.zero;
            _ballRigidbody.angularVelocity = Vector3.zero;
            _ballRigidbody.isKinematic = true;
            _ballRigidbody.detectCollisions = false;
        }
        if (_ballCollider != null)
        {
            _ballCollider.enabled = false;
        }
        HandleBallFollow();
        UpdateActionButtonLabel();
    }

    private void DropBallFromCarrier(Unit fromUnit)
    {
        _ballCarrier = null;
        _ballIsLoose = true;
        _ballPickupLockedUntil = Time.time + 0.25f;
        if (_ballTransform != null && fromUnit != null)
        {
            _ballTransform.position = fromUnit.Transform.position + fromUnit.Transform.forward * 1.2f + Vector3.up * 1.25f;
        }
        if (_ballRigidbody != null)
        {
            _ballRigidbody.isKinematic = false;
            _ballRigidbody.detectCollisions = true;
            _ballRigidbody.velocity = fromUnit != null ? fromUnit.Rigidbody.velocity * 0.55f : Vector3.zero;
        }
        if (_ballCollider != null)
        {
            _ballCollider.enabled = true;
        }
        UpdateActionButtonLabel();
    }

    private void ThrowBallFromHuman()
    {
        if (_humanPlayer == null || _ballCarrier != _humanPlayer || _ballTransform == null)
        {
            return;
        }

        float charge01 = Mathf.Clamp01(_throwHoldTime / MaxThrowCharge);
        Vector3 forward = _mainCamera != null ? _mainCamera.transform.forward : _humanPlayer.Transform.forward;
        if (forward.y < 0.08f)
        {
            forward.y = 0.08f;
        }
        forward.Normalize();
        float speed = Mathf.Lerp(10f, 28f, charge01);
        Vector3 startPos = _humanPlayer.Transform.position + _humanPlayer.Transform.forward * 1.0f + Vector3.up * 1.6f;
        Vector3 initialVelocity = forward * speed + Vector3.up * Mathf.Lerp(1.5f, 6f, charge01);

        _ballCarrier = null;
        _ballIsLoose = true;
        _ballPickupLockedUntil = Time.time + 0.55f;
        _ballTransform.position = startPos;
        if (_ballRigidbody != null)
        {
            _ballRigidbody.isKinematic = false;
            _ballRigidbody.detectCollisions = true;
            _ballRigidbody.velocity = initialVelocity;
            _ballRigidbody.angularVelocity = new Vector3(0f, 10f, 0f);
        }
        if (_ballCollider != null)
        {
            _ballCollider.enabled = true;
        }

        _statusText.text = "已投掷";
        UpdateActionButtonLabel();
    }

    private void HandleThrowRelease(Vector2 screenPosition, Camera eventCamera)
    {
        if (!_isChargingThrow || _humanPlayer == null || _humanPlayer.IsDead)
        {
            return;
        }

        if (_ballCarrier != _humanPlayer)
        {
            _isChargingThrow = false;
            _throwHoldTime = 0f;
            HideThrowPreview();
            if (_cancelThrowRect != null)
            {
                _cancelThrowRect.gameObject.SetActive(false);
                SetCancelVisual(false);
            }
            return;
        }

        bool inCancel = _cancelThrowRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(_cancelThrowRect, screenPosition, eventCamera);
        _cancelThrowOnRelease = inCancel;
        if (_cancelThrowOnRelease)
        {
            _statusText.text = "取消投掷";
        }
        else
        {
            ThrowBallFromHuman();
        }

        _isChargingThrow = false;
        _throwHoldTime = 0f;
        if (_cancelThrowRect != null)
        {
            _cancelThrowRect.gameObject.SetActive(false);
            SetCancelVisual(false);
        }
        HideThrowPreview();
    }

    private void ApplyDamage(Unit target, float amount, Unit attacker)
    {
        if (target.IsDead || amount <= 0f)
        {
            return;
        }

        target.Health -= amount;
        if (target.Health > 0f)
        {
            return;
        }

        target.Health = 0f;
        StartCoroutine(DeathAndRespawnRoutine(target, attacker));
    }

    private IEnumerator DeathAndRespawnRoutine(Unit unit, Unit attacker)
    {
        if (unit.IsDead)
        {
            yield break;
        }

        unit.IsDead = true;
        unit.Rigidbody.velocity = Vector3.zero;
        unit.Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        if (unit.Collider != null)
        {
            unit.Collider.enabled = false;
        }

        if (unit.Renderer != null)
        {
            Color c = unit.Renderer.material.color;
            c.a = 0.28f;
            unit.Renderer.material.color = c;
        }

        if (_ballCarrier == unit)
        {
            DropBallFromCarrier(unit);
            _statusText.text = unit.Name + " 被击倒，球已掉落";
        }

        float respawnSeconds = Mathf.Lerp(3f, 10f, Mathf.Clamp01(_roundElapsed / 480f));
        yield return new WaitForSeconds(respawnSeconds);

        unit.Transform.position = unit.SpawnPosition;
        unit.Transform.rotation = unit.SpawnRotation;
        unit.Health = unit.MaxHealth;
        unit.IsDead = false;
        if (unit.Collider != null)
        {
            unit.Collider.enabled = true;
        }
        if (unit.Renderer != null)
        {
            Color c = unit.Renderer.material.color;
            c.a = 1f;
            unit.Renderer.material.color = c;
        }

        ApplyRoleConstraints(unit);
    }

    private void ApplyRoleConstraints(Unit unit)
    {
        if (unit == null || unit.Rigidbody == null)
        {
            return;
        }

        if (!unit.IsAi && !unit.IsLocalPlayer)
        {
            unit.Rigidbody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        }
        else
        {
            unit.Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void BuildThrowPreview()
    {
        for (int i = 0; i < 20; i++)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.name = "ThrowPoint_" + i;
            p.transform.localScale = Vector3.one * 0.2f;
            Collider c = p.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = false;
            }
            Renderer r = p.GetComponent<Renderer>();
            r.material.color = new Color(1f, 0.92f, 0.25f, 0.86f);
            p.transform.SetParent(_fieldRoot != null ? _fieldRoot.transform : transform, false);
            p.SetActive(false);
            _trajectoryPoints.Add(p.transform);
        }

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "ThrowLandingMarker";
        marker.transform.localScale = new Vector3(0.48f, 0.04f, 0.48f);
        Collider markerCol = marker.GetComponent<Collider>();
        if (markerCol != null)
        {
            markerCol.enabled = false;
        }
        marker.GetComponent<Renderer>().material.color = new Color(1f, 0.25f, 0.2f, 0.85f);
        marker.transform.SetParent(_fieldRoot != null ? _fieldRoot.transform : transform, false);
        marker.SetActive(false);
        _trajectoryLandingMarker = marker.transform;
    }

    private void UpdateThrowPreview()
    {
        if (!_isChargingThrow || _humanPlayer == null || _ballCarrier != _humanPlayer || _trajectoryPoints.Count == 0)
        {
            HideThrowPreview();
            return;
        }

        float charge01 = Mathf.Clamp01(_throwHoldTime / MaxThrowCharge);
        Vector3 forward = _mainCamera != null ? _mainCamera.transform.forward : _humanPlayer.Transform.forward;
        if (forward.y < 0.08f)
        {
            forward.y = 0.08f;
        }
        forward.Normalize();

        float speed = Mathf.Lerp(10f, 28f, charge01);
        Vector3 start = _humanPlayer.Transform.position + _humanPlayer.Transform.forward * 1.0f + Vector3.up * 1.6f;
        Vector3 v = forward * speed + Vector3.up * Mathf.Lerp(1.5f, 6f, charge01);
        Vector3 g = Physics.gravity;
        float dt = 0.09f;

        Vector3 prev = start;
        bool landed = false;
        Vector3 landing = start;

        for (int i = 0; i < _trajectoryPoints.Count; i++)
        {
            float t = (i + 1) * dt;
            Vector3 pos = start + v * t + 0.5f * g * t * t;

            RaycastHit hit;
            if (Physics.Linecast(prev, pos, out hit))
            {
                pos = hit.point;
                landed = true;
                landing = hit.point;
            }

            _trajectoryPoints[i].gameObject.SetActive(true);
            _trajectoryPoints[i].position = pos;
            prev = pos;

            if (landed)
            {
                for (int j = i + 1; j < _trajectoryPoints.Count; j++)
                {
                    _trajectoryPoints[j].gameObject.SetActive(false);
                }
                break;
            }
        }

        if (!landed)
        {
            landing = _trajectoryPoints[_trajectoryPoints.Count - 1].position;
        }

        // 强制把落点投影到地面/碰撞体，确保落点标识在地上可见。
        RaycastHit groundHit;
        if (Physics.Raycast(landing + Vector3.up * 40f, Vector3.down, out groundHit, 120f))
        {
            landing = groundHit.point;
        }
        else
        {
            landing.y = 0.05f;
        }

        if (_trajectoryLandingMarker != null)
        {
            _trajectoryLandingMarker.gameObject.SetActive(true);
            _trajectoryLandingMarker.position = new Vector3(landing.x, landing.y + 0.03f, landing.z);
        }

        if (_cancelThrowRect != null && _cancelThrowRect.gameObject.activeSelf)
        {
            bool inCancel = RectTransformUtility.RectangleContainsScreenPoint(_cancelThrowRect, Input.mousePosition, null);
            SetCancelVisual(inCancel);
        }
    }

    private void HideThrowPreview()
    {
        for (int i = 0; i < _trajectoryPoints.Count; i++)
        {
            if (_trajectoryPoints[i] != null)
            {
                _trajectoryPoints[i].gameObject.SetActive(false);
            }
        }
        if (_trajectoryLandingMarker != null)
        {
            _trajectoryLandingMarker.gameObject.SetActive(false);
        }
    }

    private void SetCancelVisual(bool highlighted)
    {
        if (_cancelThrowRect == null)
        {
            return;
        }
        Image bg = _cancelThrowRect.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = highlighted ? new Color(1f, 0.12f, 0.12f, 1f) : new Color(0.95f, 0.2f, 0.2f, 0.85f);
        }
    }

    private SimpleJoystick CreateJoystick(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        GameObject joystickRoot = new GameObject(name, typeof(RectTransform));
        joystickRoot.transform.SetParent(parent, false);
        RectTransform joystickRect = joystickRoot.GetComponent<RectTransform>();
        SetRect(joystickRect, anchorMin, anchorMax, anchoredPos, size);

        Image joyBg = joystickRoot.AddComponent<Image>();
        joyBg.sprite = GetCircleSprite();
        joyBg.color = new Color(1f, 1f, 1f, 0.20f);
        joyBg.type = Image.Type.Simple;
        joyBg.preserveAspect = true;
        joyBg.raycastTarget = true;

        GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(joystickRoot.transform, false);
        RectTransform handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(84f, 84f);
        handleRect.anchoredPosition = Vector2.zero;
        Image handleImage = handleGo.GetComponent<Image>();
        handleImage.sprite = GetCircleSprite();
        handleImage.color = new Color(1f, 1f, 1f, 0.55f);
        handleImage.type = Image.Type.Simple;
        handleImage.preserveAspect = true;
        handleImage.raycastTarget = false;

        SimpleJoystick joystick = joystickRoot.AddComponent<SimpleJoystick>();
        joystick.Initialize(joystickRect, handleRect, 68f);
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
