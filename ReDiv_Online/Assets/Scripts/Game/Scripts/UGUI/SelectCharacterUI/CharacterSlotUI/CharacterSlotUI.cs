using Spine.Unity;
using XFramework;

public partial class CharacterSlotUI : UIBase
{
    private SkeletonAnimation  _skeletonAnimation;
    private SkeletonGraphic _skeletonGraphic;
    
    public override void Init()
    {
        InitAutoBind();

        // 在这里写其它初始化逻辑。重新生成 UI 绑定时，这个文件不会被覆盖。
        _skeletonAnimation = GetComponent<SkeletonAnimation>();
        _skeletonGraphic = GetComponent<SkeletonGraphic>();
    }
}
