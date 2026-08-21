using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;

/// <summary>
/// 系统(Player)管理器，负责PlayerData相关数据逻辑
/// </summary>
public class GameDataManager : MonoSingleton<GameDataManager>, ISaveable
{
    [LabelText("玩家数据"), ShowInInspector]
    public PlayerData PlayerData { get; private set; }
    
    
    #region ISaveable

    public string GUID => "GameDataManager";
    
    private void Start()
    {
        ((ISaveable)this).RegisterSaveable();
    }

    /// <summary>
    /// 存储数据
    /// </summary>
    /// <returns>GameSavaData 保存了所有要存储的数据</returns>
    public void SaveData(GameSaveData data)
    {
        
    }

    /// <summary>
    /// 读取数据
    /// </summary>
    /// <param name="data"></param>
    public void LoadData(GameSaveData data)
    {
    }
    

    #endregion
    




    
    
    

}

/// <summary>
/// 保存存档界面的摘要信息
/// </summary>
[System.Serializable]
public class UserSaveSummary
{
    /// <summary>
    /// 用户ID(存档的编号)
    /// </summary>
    public int UserID;
    
    [LabelText("用户名")]
    public string UserName;
    
    [LabelText("创建时间")]
    public DateTime CreateTime;
    
    [LabelText("摘要金币")]
    public int PreviewGoldNumber;
    
    [LabelText("摘要天数")]
    public int PreviewDay;
    
    [LabelText("摘要星期数")]
    public int PreviewWeek;

    /// <summary>
    /// 浅拷贝一份摘要（字段均为值类型/字符串，浅拷贝即可）。
    /// 用于「另存到其它槽位」时，避免篡改原槽位摘要的引用。
    /// </summary>
    public UserSaveSummary Clone()
    {
        return new UserSaveSummary
        {
            UserID = UserID,
            UserName = UserName,
            CreateTime = CreateTime,
            PreviewGoldNumber = PreviewGoldNumber,
            PreviewDay = PreviewDay,
            PreviewWeek = PreviewWeek,
        };
    }
}

[System.Serializable]
public class PlayerData
{
    [LabelText("用户名")]
    public string UserName;
    [LabelText("游戏内天数")]
    public int Day;
    [LabelText("游戏内当前时间")]
    public DateTime GameDateTime;

}


