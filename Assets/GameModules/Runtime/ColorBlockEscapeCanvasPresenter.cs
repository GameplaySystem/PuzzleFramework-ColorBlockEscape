using System;
using System.Collections.Generic;
using PuzzleFramework.RuntimeFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Authored gameplay Canvas binding. It displays controller-owned state and routes UI
    /// actions back through the runtime controller without owning gameplay decisions.
    /// </summary>
    public sealed class ColorBlockEscapeCanvasPresenter : MonoBehaviour
    {
        [Header("Runtime")]
        [SerializeField] private ColorBlockEscapeRuntimeController _controller;

        [Header("HUD")]
        [SerializeField] private TMP_Text _levelLabel;
        [SerializeField] private TMP_Text _timerLabel;
        [SerializeField] private Image _timerBackground;
        [SerializeField] private Button _chooseLevelButton;

        [Header("Level chooser")]
        [SerializeField] private GameObject _levelChooserPanel;
        [SerializeField] private RectTransform _levelButtonContent;
        [SerializeField] private Button _levelButtonTemplate;
        [SerializeField] private Button _closeChooserButton;

        [Header("Win result")]
        [SerializeField] private GameObject _winPanel;
        [SerializeField] private TMP_Text _winMessage;
        [SerializeField] private Button _nextLevelButton;
        [SerializeField] private Button _replayButton;
        [SerializeField] private Button _winChooseLevelButton;

        [Header("Loss result")]
        [SerializeField] private GameObject _lossPanel;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _lossChooseLevelButton;

        [Header("Timer colors")]
        [SerializeField] private Color _timerNormalColor = new(0.08f, 0.12f, 0.2f, 0.94f);
        [SerializeField] private Color _timerWarningColor = new(0.82f, 0.18f, 0.16f, 0.96f);
        [SerializeField] private Color _timerNormalTextColor = Color.white;
        [SerializeField] private Color _timerWarningTextColor = new(1f, 0.91f, 0.62f, 1f);

        private readonly List<Button> _levelButtons = new();
        private bool _subscribed;
        private bool _isTimerWarningActive;
        private int _lastDisplayedSecond = int.MinValue;

        public ColorBlockEscapeRuntimeController Controller => _controller;
        public bool IsLevelChooserOpen => _levelChooserPanel != null && _levelChooserPanel.activeSelf;
        public bool IsWinPanelVisible => _winPanel != null && _winPanel.activeSelf;
        public bool IsLossPanelVisible => _lossPanel != null && _lossPanel.activeSelf;
        public bool IsTimerWarningActive => _isTimerWarningActive;
        public bool IsNextLevelActionVisible => _nextLevelButton != null && _nextLevelButton.gameObject.activeSelf;
        public int LevelButtonCount => _levelButtons.Count;
        public string DisplayedLevelLabel => _levelLabel != null ? _levelLabel.text : string.Empty;
        public string DisplayedTimer => _timerLabel != null ? _timerLabel.text : string.Empty;

        private void Awake()
        {
            if (_controller == null)
                _controller = FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            EnsureLevelListClipping();
            SetPanelActive(_levelChooserPanel, false);
            SetPanelActive(_winPanel, false);
            SetPanelActive(_lossPanel, false);
        }

        private void OnEnable()
        {
            if (_controller == null)
                _controller = FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            if (_controller == null)
            {
                Debug.LogError("CBE Canvas presenter requires a runtime controller.", this);
                enabled = false;
                return;
            }

            _controller.LegacyGameplayGuiEnabled = false;
            Subscribe();
            BindButtons();
            RebuildLevelButtons();
            RefreshAll();
        }

        private void Update()
        {
            RefreshTimer();
        }

        private void OnDisable()
        {
            RestoreBoardInput();
            UnbindButtons();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            RestoreBoardInput();
        }

        public void OpenLevelChooser()
        {
            if (_controller == null || _levelChooserPanel == null) return;
            _controller.SetBoardPointerInputSuspended(true);
            _levelChooserPanel.SetActive(true);
            _levelChooserPanel.transform.SetAsLastSibling();
            RebuildLevelButtons();
            if (_levelButtonContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_levelButtonContent);
                Canvas.ForceUpdateCanvases();
                ScrollRect scrollRect = _levelButtonContent.GetComponentInParent<ScrollRect>();
                if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        public void CloseLevelChooser()
        {
            if (_levelChooserPanel != null) _levelChooserPanel.SetActive(false);
            RestoreBoardInput();
        }

        public bool TryChooseLevel(int index)
        {
            if (_controller == null) return false;
            bool loaded = _controller.TrySelectLevel(index);
            if (loaded) CloseLevelChooser();
            return loaded;
        }

        public bool ReplayCurrentLevel()
        {
            return _controller != null && _controller.RestartCurrentLevel();
        }

        public bool TryNextLevel()
        {
            if (_controller == null) return false;
            int nextIndex = _controller.SelectedLevelIndex + 1;
            return nextIndex < _controller.LevelCount && _controller.TrySelectLevel(nextIndex);
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _controller.LevelLoaded += HandleLevelLoaded;
            _controller.OutcomeChanged += HandleOutcomeChanged;
            _controller.ResultVisibilityChanged += HandleResultVisibilityChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _controller == null) return;
            _controller.LevelLoaded -= HandleLevelLoaded;
            _controller.OutcomeChanged -= HandleOutcomeChanged;
            _controller.ResultVisibilityChanged -= HandleResultVisibilityChanged;
            _subscribed = false;
        }

        private void BindButtons()
        {
            _chooseLevelButton?.onClick.AddListener(OpenLevelChooser);
            _closeChooserButton?.onClick.AddListener(CloseLevelChooser);
            _nextLevelButton?.onClick.AddListener(HandleNextLevel);
            _replayButton?.onClick.AddListener(HandleReplay);
            _winChooseLevelButton?.onClick.AddListener(OpenLevelChooser);
            _retryButton?.onClick.AddListener(HandleReplay);
            _lossChooseLevelButton?.onClick.AddListener(OpenLevelChooser);
        }

        private void UnbindButtons()
        {
            _chooseLevelButton?.onClick.RemoveListener(OpenLevelChooser);
            _closeChooserButton?.onClick.RemoveListener(CloseLevelChooser);
            _nextLevelButton?.onClick.RemoveListener(HandleNextLevel);
            _replayButton?.onClick.RemoveListener(HandleReplay);
            _winChooseLevelButton?.onClick.RemoveListener(OpenLevelChooser);
            _retryButton?.onClick.RemoveListener(HandleReplay);
            _lossChooseLevelButton?.onClick.RemoveListener(OpenLevelChooser);
        }

        private void HandleLevelLoaded()
        {
            if (IsLevelChooserOpen) CloseLevelChooser();
            RebuildLevelButtons();
            _lastDisplayedSecond = int.MinValue;
            RefreshAll();
        }

        private void HandleOutcomeChanged(GameState state)
        {
            RefreshResultPanels();
        }

        private void HandleResultVisibilityChanged(bool visible)
        {
            RefreshResultPanels();
        }

        private void HandleNextLevel()
        {
            TryNextLevel();
        }

        private void HandleReplay()
        {
            ReplayCurrentLevel();
        }

        private void RefreshAll()
        {
            if (_controller == null) return;
            if (_levelLabel != null) _levelLabel.text = _controller.SelectedLevelDisplayLabel;
            RefreshTimer();
            RefreshResultPanels();
        }

        private void RefreshTimer()
        {
            if (_controller == null || _timerLabel == null) return;
            bool hasTimer = _controller.HasTimer;
            _timerLabel.gameObject.SetActive(hasTimer);
            if (_timerBackground != null) _timerBackground.gameObject.SetActive(hasTimer);
            if (!hasTimer)
            {
                _isTimerWarningActive = false;
                return;
            }

            float remaining = Mathf.Max(0f, _controller.RemainingSeconds);
            int displayedSecond = Mathf.CeilToInt(remaining);
            if (displayedSecond != _lastDisplayedSecond)
            {
                _lastDisplayedSecond = displayedSecond;
                _timerLabel.text = $"{displayedSecond / 60:00}:{displayedSecond % 60:00}";
            }

            bool warning = remaining <= _controller.TimerWarningThresholdSeconds;
            if (warning == _isTimerWarningActive && _timerBackground != null) return;
            _isTimerWarningActive = warning;
            _timerLabel.color = warning ? _timerWarningTextColor : _timerNormalTextColor;
            if (_timerBackground != null)
                _timerBackground.color = warning ? _timerWarningColor : _timerNormalColor;
        }

        private void RefreshResultPanels()
        {
            if (_controller == null) return;
            bool mayShow = _controller.IsResultPresentationVisible;
            bool won = mayShow && _controller.CommittedOutcome == GameState.Won;
            bool lost = mayShow && _controller.CommittedOutcome == GameState.Lost;
            SetPanelActive(_winPanel, won);
            SetPanelActive(_lossPanel, lost);

            bool hasNext = _controller.SelectedLevelIndex >= 0 &&
                           _controller.SelectedLevelIndex + 1 < _controller.LevelCount;
            if (_nextLevelButton != null) _nextLevelButton.gameObject.SetActive(won && hasNext);
            if (_winMessage != null)
                _winMessage.text = hasNext
                    ? "The board is clear."
                    : "All assigned levels are complete.";
        }

        private void RebuildLevelButtons()
        {
            foreach (Button button in _levelButtons)
            {
                if (button == null) continue;
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            _levelButtons.Clear();

            if (_controller == null || _levelButtonTemplate == null ||
                _levelButtonContent == null) return;
            _levelButtonTemplate.gameObject.SetActive(false);
            for (int index = 0; index < _controller.LevelCount; index++)
            {
                int selectedIndex = index;
                Button button = Instantiate(_levelButtonTemplate, _levelButtonContent);
                button.name = $"Level {index + 1} Button";
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = _controller.GetLevelDisplayLabel(index);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => TryChooseLevel(selectedIndex));
                button.gameObject.SetActive(true);
                _levelButtons.Add(button);
            }
        }

        private void EnsureLevelListClipping()
        {
            if (_levelButtonContent == null || _levelButtonContent.parent == null) return;
            GameObject viewport = _levelButtonContent.parent.gameObject;
            Mask stencilMask = viewport.GetComponent<Mask>();
            if (stencilMask != null) stencilMask.enabled = false;
            if (viewport.GetComponent<RectMask2D>() == null)
                viewport.AddComponent<RectMask2D>();
        }

        private void RestoreBoardInput()
        {
            _controller?.SetBoardPointerInputSuspended(false);
        }

        private static void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null && panel.activeSelf != active) panel.SetActive(active);
        }
    }
}
