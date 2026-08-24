using Sirenix.OdinInspector;
using Spine.Unity;
using UnityEngine;
using XFramework;

public class CharacterGraphicUI : UIBase
{
    private SkeletonGraphic _skeletonGraphic;
    private SkeletonAnimation _skeletonAnimation;
    
    [SpineAnimation,LabelText("待机动画")]
    public string IdleName;
    
    /// <summary>
    /// 初始化方法,一般不需要手动调用
    /// </summary>
    public override void Init()
    {
        _skeletonGraphic = GetComponent<SkeletonGraphic>();
        _skeletonAnimation = GetComponent<SkeletonAnimation>();
        
    }
}
