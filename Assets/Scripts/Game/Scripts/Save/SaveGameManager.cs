using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏存储管理器
    /// </summary>
    public class SaveGameManager : MonoSingleton<SaveGameManager>,IGameInitialized
    {
        #region Config
        /// <summary>
        /// 缓存存储Json文件的位置
        /// </summary>
        private static string JsonSavePath;

        /// <summary>
        /// 存档序列化设置
        /// </summary>
        private JsonSerializerSettings settings;

        /// <summary>
        /// 存档子目录名（保持原值，勿改，否则旧存档会失效）。
        ///
        /// 公开是因为<b>跨存档</b>的数据也落在这个目录下、但不走本类
        /// （按存档槽走的那套对它们没意义），比如剧情的已读标记 <see cref="DramaReadMarks"/>。
        /// </summary>
        public const string SaveFolderName = "GameSaveData";

        /// <summary>
        /// 存档目录绝对路径
        /// </summary>
        private static string SaveDir => Path.Combine(JsonSavePath, SaveFolderName);

        /// <summary>
        /// 某个用户存档文件路径
        /// </summary>
        private static string GetUserPath(int userID) => Path.Combine(SaveDir, $"User{userID}.scriptable");

        /// <summary>
        /// 用户列表文件路径
        /// </summary>
        private static string UsersPath => Path.Combine(SaveDir, "Logic.scriptable");

        /// <summary>
        /// 确保存档目录存在
        /// </summary>
        private static void EnsureSaveDir()
        {
            if (!Directory.Exists(SaveDir))
                Directory.CreateDirectory(SaveDir);
        }
        #endregion

        #region User
      
        
        /// <summary>
        /// 存档下所有的用户列表
        /// </summary>
        public List<UserSaveSummary> Users { get; private set; }
        private List<ISaveable> iSaveables = new();
        /// <summary>
        /// 当前用户对象
        /// </summary>
        public UserSaveSummary CurUserSaveSummary { get; private set; }
        [SerializeReference] 
        private GameSaveData curGameSaveData;
        

        #endregion

        #region  注册存档

        /// <summary>
        /// 注册函数将自身要存储的信息注册到ISaveablesList中
        /// </summary>
        /// <param name="saveable"></param>
        public void RegisterSaveable(ISaveable saveable)
        {
            if (!iSaveables.Contains(saveable))
            {
                iSaveables.Add(saveable);
            }
        }
        public void RemoveSaveable(ISaveable saveable)
        {
            iSaveables.Remove(saveable);
        }

        #endregion
        
        #region 保存用户数据

        public void Save()
        {
            Save(Users[0]);
        }

        /// <summary>
        /// 保存用户数据
        /// </summary>
        /// <param name="userSaveSummary">用户</param>
        private void Save(UserSaveSummary userSaveSummary)
        {
            if (userSaveSummary == null)
            {
                Debug.LogError("保存存档失败：userSaveSummary == null");
                return;
            }
            if (curGameSaveData == null)
            {
                Debug.LogError("保存存档失败：gameSaveData == null，请先 Load / CreateUser 后再保存");
                return;
            }

            RefreshUserSummary(userSaveSummary);
            foreach (var SaveItem in iSaveables)
            {
                SaveItem.SaveData(curGameSaveData);
            }

            var path = GetUserPath(userSaveSummary.UserID);
            try
            {
                EnsureSaveDir();
                var JsonData = JsonConvert.SerializeObject(curGameSaveData,settings);
                File.WriteAllText(path, JsonData);
            }
            catch (Exception e)
            {
                Debug.LogError($"保存存档写入文件失败 path={path}\n{e}");
                return;
            }

            SaveUsers();
        }

        #endregion
        
        #region 加载用户数据
        /// <summary>
        /// 加载用户数据
        /// </summary>
        /// <param name="userSaveSummary">用户</param>
        public void Load(UserSaveSummary userSaveSummary)
        {
            CurUserSaveSummary = userSaveSummary;
            string path = GetUserPath(userSaveSummary.UserID);

            curGameSaveData = LoadGameSaveData(path);

            foreach (ISaveable item in iSaveables)
                item.LoadData(curGameSaveData);
        }

        /// <summary>
        /// 读取存档文件并反序列化；文件不存在 / 读取失败 / 坏档时回退到新存档。
        /// </summary>
        private GameSaveData LoadGameSaveData(string path)
        {
            if (!File.Exists(path))
                return GameSaveData.Create();

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<GameSaveData>(json,settings: settings) ?? GameSaveData.Create();
            }
            catch (Exception e)
            {
                Debug.LogError($"读取存档失败（文件可能已损坏），将回退到新存档 path={path}\n{e}");
                return GameSaveData.Create();
            }
        }

        /// <summary>
        /// 删除用户数据
        /// </summary>
        /// <param name="UID"></param>
        public void Delete(int UID)
        {
            var path = GetUserPath(UID);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"删除存档文件失败 path={path}\n{e}");
            }
        }
        

        #endregion
        
        #region 保存用户

        /// <summary>
        /// 保存所有用户
        /// </summary>
        private void SaveUsers()
        {
            try
            {
                EnsureSaveDir();
                var JsonData = JsonConvert.SerializeObject(Users,settings);
                File.WriteAllText(UsersPath, JsonData);
            }
            catch (Exception e)
            {
                Debug.LogError($"保存用户列表失败 path={UsersPath}\n{e}");
            }
        }

        #endregion

        #region 加载用户

        /// <summary>
        /// 加载所有用户
        /// </summary>
        private void LoadUsers()
        {
            List<UserSaveSummary> slotData = null;
            if (File.Exists(UsersPath))
            {
                try
                {
                    var JsonData = File.ReadAllText(UsersPath);
                    slotData = JsonConvert.DeserializeObject<List<UserSaveSummary>>(JsonData,settings);
                }
                catch (Exception e)
                {
                    Debug.LogError($"读取用户列表失败（文件可能已损坏），将按空列表处理 path={UsersPath}\n{e}");
                }
            }

            Users = slotData is { Count: > 0 } ? slotData : new List<UserSaveSummary>();
            UsersChangeAction?.Invoke(Users);
        }

        #endregion

        #region 增加用户
        

        public void CreateUser(int idx, string UserName)
        {
            UserSaveSummary newUserSaveSummary = new()
            {
                UserID = idx,
                UserName = UserName,
                CreateTime = DateTime.Now,
                PreviewDay = 1,
                PreviewWeek = 1,
            };

            int index = Users.FindIndex(temp => temp.UserID == idx);
            if (index >= 0)
                Users[index] = newUserSaveSummary;
            else
                Users.Add(newUserSaveSummary);
            CurUserSaveSummary = newUserSaveSummary;
            curGameSaveData = GameSaveData.Create();
            foreach (var saveItem in iSaveables)
                saveItem.LoadData(curGameSaveData);

            Save(newUserSaveSummary);
            LoadUsers();
            GameManager.Instance.EnterGame(newUserSaveSummary);
        }

        public void SaveUser(int idx, UserSaveSummary newUserSaveSummary)
        {
            if (newUserSaveSummary == null)
            {
                Debug.LogError("SaveUser 失败：newUserSaveSummary == null");
                return;
            }

            // 另存到「其它」槽位时克隆一份，避免就地篡改原槽位摘要的引用（否则会串档/产生重复条目）；
            // 存到「同一」槽位时直接复用本体。
            var summary = newUserSaveSummary.UserID == idx ? newUserSaveSummary : newUserSaveSummary.Clone();
            summary.UserID = idx;

            int index = Users.FindIndex(temp => temp.UserID == idx);
            if (index >= 0)
                Users[index] = summary;
            else
                Users.Add(summary);
            CurUserSaveSummary = summary;
            
            Save(summary);
        }

        /// <summary>
        /// 删除一个已有存档
        /// </summary>
        /// <param name="UID">已有存档的用户唯一标识UID</param>
        public void DeleteUser(int UID)
        {
            int index = Users.FindIndex(temp => temp.UserID == UID);
            if (index >= 0)
            {
                Delete(Users[index].UserID);
                Users.RemoveAt(index);
                SaveUsers();
                UsersChangeAction?.Invoke(Users);
            }
        }

        #endregion

        #region Enven 事件回调函数

        private Action<List<UserSaveSummary>> UsersChangeAction;

        /// <summary>
        /// 注册所有用户变化回调
        /// </summary>
        /// <param name="callBack"></param>
        public void RegisterUsersChange(Action<List<UserSaveSummary>> callBack)
        {
            UsersChangeAction += callBack;
            callBack?.Invoke(Users);
        }

        /// <summary>
        /// 反注册所有用户变化回调
        /// </summary>
        /// <param name="callBack"></param>
        public void UnregisterUsersChange(Action<List<UserSaveSummary>> callBack)
        {
            UsersChangeAction -= callBack;
        }

        #endregion

        #region 摘要同步

        private void RefreshUserSummary(UserSaveSummary summary)
        {
            if (summary == null)
                return;

            PlayerData playerData = GameDataManager.Instance.PlayerData;

            if (playerData == null)
            {
                Debug.LogWarning("刷新存档摘要失败：PlayerData == null");
                return;
            }

            summary.UserName = playerData.UserName;
            summary.PreviewDay = playerData.Day;

            int index = Users.FindIndex(temp => temp.UserID == summary.UserID);

            if (index >= 0)
            {
                Users[index] = summary;
            }
            else
            {
                Users.Add(summary);
            }
        }

        #endregion

        /// <summary>
        /// 初始化脚本函数
        /// </summary>
        /// <returns></returns>
        public async UniTask Initialized()
        {
            settings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };
            JsonSavePath = Application.persistentDataPath;
            LoadUsers();
            await UniTask.CompletedTask;
        }

        /// <summary>
        /// 释放脚本函数
        /// </summary>
        public async UniTask Release()
        {
            await UniTask.CompletedTask;
        }
    }
}

