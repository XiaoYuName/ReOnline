using UnityEngine;
using XFramework;

public static class GamePathTools
{
    /// <summary>
    /// 组合场景路径
    /// </summary>
    /// <param name="scenePath">场景路径</param>
    /// <returns>完整的场景路径</returns>
    public static string CombinationScenePath(string scenePath)
    {
        // 如果已经是完整路径，直接返回
        if (scenePath.StartsWith("Assets/"))
        {
            return scenePath;
        }

        // 否则组合成 Addressables 路径
        return $"Assets/AddressableAssets/Remote/Scenes/{scenePath}.unity";
    }
}
