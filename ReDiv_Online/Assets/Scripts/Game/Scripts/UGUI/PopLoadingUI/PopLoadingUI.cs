using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Febucci.UI;
using TMPro;
using UnityEngine;
using XFramework;

namespace XFramework
{
    public class PopLoadingUI : UIBase
    {
        /// <summary>
        /// 渐变遮罩的CanvasGroup,挂在根节点上(和Canvas同一个物体)。
        /// 只保留这一个:UGUI 判断能否被点到时会一路往上查所有CanvasGroup,
        /// 任意一个 blocksRaycasts 为 false 就整条不拦,子节点上再挂一个只会互相干扰。
        /// </summary>
        private CanvasGroup _canvasGroup;
        private Sequence _sequence;
        private Canvas _canvas;
        private TextMeshProUGUI _text;
        private TypewriterByCharacter _typewriter;
        private CancellationTokenSource cancellationToken;

        /// <summary>
        /// 渐变遮罩默认的 Order in Layer,和各 Fade 方法的默认值保持一致。
        /// </summary>
        private const int DefaultOrderInLayer = 60;

        /// <summary>
        /// 初始化方法,一般不需要手动调用
        /// </summary>
        public override void Init()
        {
            _canvas = Get<Canvas>("");
            _canvasGroup = Get<CanvasGroup>("");
            _text = Get<TextMeshProUGUI>("UIMask/Tip");
            _typewriter = Get<TypewriterByCharacter>("UIMask/Tip");
            UISystem.Instance.AddUI("PopLoadingUI", this);

            // 常驻UI:开局保持整块遮住(连UI一起)并拦住输入,挡掉初始化过程,
            // 等 GameManager 那边加载完再 FadeOut 淡出。
            ApplyCanvasLayer(FadeLayer.All, DefaultOrderInLayer);
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;

            // Open() 里做的这两件事在这也补一遍:常驻实例可能不经过 OpenUI 就被直接调 FadeOut/ShowLabel
            cancellationToken ??= new CancellationTokenSource();
            _typewriter.gameObject.SetActive(false);
        }

        /// <summary>
        /// 通用UI打开方法,提供重写
        /// </summary>
        public override void Open()
        {
            base.Open();
            cancellationToken = new CancellationTokenSource();
            _typewriter.gameObject.SetActive(false);
        }
        

        /// <summary>
        /// 把渐变遮罩切到对应的渐变Sorting Layer上。
        /// FadeLayer.Scene → SceneFade:遮住整个场景,普通UI照常显示(小场景之间切换用)。
        /// FadeLayer.All   → UIFade:  最顶层,连UI一起遮掉(进出小游戏、读档这种整体转场用)。
        /// </summary>
        private void ApplyCanvasLayer(FadeLayer layer, int orderInLayer)
        {
            string sortingLayerName = UISystem.Instance.GetFadeSortingLayerName(layer);
            if (!string.IsNullOrEmpty(sortingLayerName))
            {
                _canvas.sortingLayerName = sortingLayerName;
            }

            // 常驻在UISystem下时这是个根Canvas,overrideSorting 其实用不上;
            // 留着是防止以后被挪到 UILayout 底下变成嵌套Canvas,那时候不开这个上面两行就不生效了。
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = orderInLayer;
        }

        public void FadeIn(float duration, FadeLayer layer = FadeLayer.All, int OrderInLayer = DefaultOrderInLayer)
        {
            _sequence?.Kill();
            _sequence = DOTween.Sequence();
            ApplyCanvasLayer(layer, OrderInLayer);
            _canvasGroup.blocksRaycasts = true;
            _sequence.Append(_canvasGroup.DOFade(1, duration));
        }

        public void FadeOut(float duration, FadeLayer layer = FadeLayer.All, int OrderInLayer = DefaultOrderInLayer)
        {
            ApplyCanvasLayer(layer, OrderInLayer);
            _canvasGroup.blocksRaycasts = true;
            _sequence?.Kill();
            _sequence = DOTween.Sequence();
            _sequence.Append(_canvasGroup.DOFade(0, duration))
                .OnComplete(() => { _canvasGroup.blocksRaycasts = false; });
        }

        public async UniTask FadeInAsync(float duration, FadeLayer layer = FadeLayer.All,
            int OrderInLayer = DefaultOrderInLayer)
        {
            ApplyCanvasLayer(layer, OrderInLayer);
            _sequence?.Kill();
            _sequence = DOTween.Sequence();
            _canvasGroup.blocksRaycasts = true;
            _sequence.Append(_canvasGroup.DOFade(1, duration));
            await _sequence.AsyncWaitForCompletion();
        }

        public async UniTask FadeOutAsync(float duration, FadeLayer layer = FadeLayer.All,
            int OrderInLayer = DefaultOrderInLayer)
        {
            ApplyCanvasLayer(layer, OrderInLayer);
            _sequence?.Kill();
            _sequence = DOTween.Sequence();
            _sequence.Append(_canvasGroup.DOFade(0, duration));
            await _sequence.AsyncWaitForCompletion();
            _canvasGroup.blocksRaycasts = false;
        }

        public void Fade(float duration, Action callback, FadeLayer layer = FadeLayer.All,
            int OrderInLayer = DefaultOrderInLayer)
        {
            FadeIn(duration, layer, OrderInLayer);
            callback?.Invoke();
            FadeOut(duration, layer, OrderInLayer);
        }

        public async UniTask FadeAsync(float duration, Action action, FadeLayer layer = FadeLayer.All,
            int OrderInLayer = DefaultOrderInLayer)
        {
            await FadeInAsync(duration, layer, OrderInLayer);
            action?.Invoke();
            await FadeOutAsync(duration, layer, OrderInLayer);
        }

        public async UniTask FadeAsync(float duration, Func<UniTask> action, FadeLayer layer = FadeLayer.All,
            int OrderInLayer = DefaultOrderInLayer)
        {
            await FadeInAsync(duration, layer, OrderInLayer);
            await action();
            await FadeOutAsync(duration, layer, OrderInLayer);
        }

        public async UniTask FadeAsync(float duration, List<UniTask> actions, FadeLayer layer = FadeLayer.All,
            int OrderInLayer = DefaultOrderInLayer)
        {
            await FadeInAsync(duration, layer, OrderInLayer);
            foreach (var function in actions)
            {
                await function;
            }

            await FadeOutAsync(duration, layer, OrderInLayer);
        }
        
        public async UniTask ShowLabel(string label)
        {
           

            _typewriter.gameObject.SetActive(true);
            _typewriter.ShowText(label);

            // 等待文字显示完成
            await UniTask.WaitWhile(
                () => _typewriter.isShowingText,
                cancellationToken: cancellationToken.Token);

            // 可选：完整显示后停留一段时间
            await UniTask.Delay(
                TimeSpan.FromSeconds(1),
                cancellationToken: cancellationToken.Token);

            _typewriter.StartDisappearingText();

            // 等待文字隐藏完成
            await UniTask.WaitWhile(
                () => _typewriter.isHidingText,
                cancellationToken: cancellationToken.Token
            );

            _typewriter.gameObject.SetActive(false);
        }

    }
}


