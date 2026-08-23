using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace XFramework
{
    /// <summary>
    /// 游戏总管理器
    /// </summary>
    public class GameManager : MonoOdinSingleton<GameManager>
    {
        [Header("资源加载")]
        [Tooltip("编辑器下默认使用本地资源；切到 Addressables 可以预览真实 Addressables 加载链路。打包后始终使用 Addressables。")]
        [SerializeField] 
        private AssetsLoadMode assetsLoadMode = AssetsLoadMode.LocalAssetDatabase;

        public CommonUI _commonUI;
        public PopLoadingUI  _popLoadingUI;

        /// <summary>
        /// 游戏开始事件
        /// </summary>
        public  event Action OnEnterGame;
        /// <summary>
        /// 游戏结束事件
        /// </summary>
        public event Action OnExitGame;
        
        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
            _popLoadingUI.Init();
            AssetsManager.Instance.SetLoadMode(assetsLoadMode);
            ResolutionManager.Instance.Initialized().Forget();
        }

        private void Start()
        {
            Initialized().Forget();
        }
        
        public async UniTask Initialized()
        {
            await Addressables.InitializeAsync();
            await PlayerInputManager.Instance.Initialized();
            await AudioManager.Instance.Initialized();
            await UISystem.Instance.Initialized();
            await SaveGameManager.Instance.Initialized();

            Application.targetFrameRate = -1;
           
            StarGame();
            // 首屏准备好了再把遮罩淡掉
            await _popLoadingUI.FadeOutAsync(1f);
        }

        public async UniTask Release()
        {
            await SaveGameManager.Instance.Release();
            await UISystem.Instance.Release();
            await AudioManager.Instance.Release();
            await PlayerInputManager.Instance.Release();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Release().Forget();
        }

        private void StarGame()
        {
            _commonUI.uiPageData = UISystem.Instance.GetPageData(UIKeys.CommonUI);
            _commonUI.Open();
            _commonUI.Init();
        }

        public void EnterGame(UserSaveSummary selectedUserSaveSummary)
        {
            UISystem.Instance.CloseUI("CommonUI");
            OnEnterGame?.Invoke();
        }

        private void OnOnExitGame()
        {
            OnExitGame?.Invoke();
        }
    }
}

