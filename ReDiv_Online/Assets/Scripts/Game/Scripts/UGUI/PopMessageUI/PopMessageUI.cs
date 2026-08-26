using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;

public partial class PopMessageUI : UIBase
{
    [BoxGroup("颜色配置"),LabelText("默认颜色")]
    public Color NormalColor;
    [BoxGroup("颜色配置"),LabelText("选中颜色")]
    public Color SelectedColor;
    
    public override void Init()
    {
        InitAutoBind();

        // 在这里写其它初始化逻辑。重新生成 UI 绑定时，这个文件不会被覆盖。
    }
}
