using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PathologicalGames;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

namespace XFramework
{
    /// <summary>
    /// 音效总管理器
    /// </summary>
    public class AudioManager : MonoOdinSingleton<AudioManager>,IGameInitialized
    {
        #region IGameInitialized

        [BoxGroup("Initialized"),LabelText("配置路径"),FilePath]
        public string ConfigPath = "Assets/AddressableAssets/Configs/UIPage/PageConfiguration.asset";
        [BoxGroup("Initialized"),ShowInInspector,LabelText("配置表数据"),ReadOnly]
        private AudioConfiguration _audioConfiguration;
        
        public async UniTask Initialized()
        {
            _audioConfiguration = await AssetsManager.Instance.
                    LoadAssetsUniTask<AudioConfiguration>(ConfigPath);
            InitializedComplete();
            InitializedSnapshot();

            await InitializedMusicSource();
        }

        private void InitializedComplete()
        {
            XMixer = _audioConfiguration.XMixer;
            bgmSource = transform.Find("BgmSource").GetComponent<AudioSource>();
            ambientSource = transform.Find("AmbientSource").GetComponent<AudioSource>();
            humanSource = transform.Find("HumanSource").GetComponent<AudioSource>();
            videoSource = transform.Find("VideoSource").GetComponent<AudioSource>();
            
            bgmSource.outputAudioMixerGroup =  _audioConfiguration.GetByMixerGroup(AudioMixerGroupType.BGMItem);
            ambientSource.outputAudioMixerGroup =  _audioConfiguration.GetByMixerGroup(AudioMixerGroupType.AmbientItem);
            humanSource.outputAudioMixerGroup =  _audioConfiguration.GetByMixerGroup(AudioMixerGroupType.HumanItem);
            videoSource.outputAudioMixerGroup =  _audioConfiguration.GetByMixerGroup(AudioMixerGroupType.VideoItem);
            
            SetAudioVolume(AudioMixerGroupType.Master,PlayerPrefs.GetFloat("MasterVolume", 0.5f));
            SetAudioVolume(AudioMixerGroupType.BGMItem,PlayerPrefs.GetFloat("BGMItemVolume", 0.5f));
            SetAudioVolume(AudioMixerGroupType.MusicItem,PlayerPrefs.GetFloat("MusicItemVolume", 0.5f));
            SetAudioVolume(AudioMixerGroupType.HumanItem,PlayerPrefs.GetFloat("HumanItemVolume", 0.5f));
        }

        private async UniTask InitializedMusicSource()
        {
            var source =
                 await AssetsManager.Instance.LoadAssetsUniTask<GameObject>(_audioConfiguration.MusicSourcePath);
            MusicSource = source.GetComponent<AudioSource>();

            GameObject newRoot = new GameObject("AudioPoolRoot");
            newRoot.transform.SetParent(transform);
            newRoot.transform.localPosition = Vector3.zero;
            
            audioSpawnPool = PoolManager.Pools.Create("AudioManager",newRoot);
            PrefabPool audioPool = new PrefabPool(MusicSource.transform)
            {
                preloadAmount = 20,
            };
            audioSpawnPool.CreatePrefabPool(audioPool);
        }

        private void InitializedSnapshot()
        {
            foreach (var item in _audioConfiguration.MixerSnapshotsList)
            {
                if (!snapshots.ContainsKey(item.Type))
                {
                    snapshots.Add(item.Type, item.Snapshot);
                }
                else
                {
                    Debug.LogWarning("重复快照,将被跳过!");
                }
            }
        }

        /// <summary>
        /// 收尾。由 <c>GameManager.Release</c> 统一调（它自己是在 OnDestroy 里触发的）。
        ///
        /// <b>顺序不能反</b>：先停声再拆池子 —— 反过来的话，正在播的音效实例已经被销毁，
        /// 它内部那条等待计时器到点还会去 Despawn 一个不存在的东西。
        ///
        /// 两个 AA 引用（配置表、音效播放器预制体）是 <see cref="Initialized"/> 里各 Load 一次的，
        /// 这里一一对应地还回去。多还少还都不行。
        /// </summary>
        public UniTask Release()
        {

            StopAllAudio();

            // 快照表马上就要清空，欠着的"回 Normal"没有意义了，直接勾销
            humanDuck.Cancel();
            videoDuck.Cancel();

            if (audioSpawnPool != null)
            {
                // Destroy 会把池子的根节点一起销毁，AudioPoolRoot 不用单独处理
                PoolManager.Pools.Destroy(audioSpawnPool.poolName);
                audioSpawnPool = null;
            }

            snapshots.Clear();

            // 先取出路径再置空配置：MusicSourcePath 在配置表上，顺序反了就取不到了
            string musicSourcePath = _audioConfiguration != null ? _audioConfiguration.MusicSourcePath : null;
            _audioConfiguration = null;

            if (!string.IsNullOrEmpty(musicSourcePath))
            {
                AssetsManager.Instance?.FreeAsset(musicSourcePath);
            }

            AssetsManager.Instance?.FreeAsset(ConfigPath);

            // 这几个是场景里挂着的子节点，不归我们销毁，置空只是断引用
            bgmSource = null;
            ambientSource = null;
            humanSource = null;
            videoSource = null;
            MusicSource = null;
            XMixer = null;

            return UniTask.CompletedTask;
        }

        #endregion

        #region Complete

        [ReadOnly,ShowInInspector,BoxGroup("Sources"),LabelText("背景播放器")]
        private AudioSource bgmSource;
        
        [ReadOnly,ShowInInspector,BoxGroup("Sources"),LabelText("环境播放器")]
        private AudioSource ambientSource;
        
        [ReadOnly,ShowInInspector,BoxGroup("Sources"),LabelText("人声播放器")]
        private AudioSource humanSource;
        
        [ReadOnly,ShowInInspector,BoxGroup("Sources"),LabelText("UI播放器")]
        private AudioSource videoSource;
        
        [ReadOnly,ShowInInspector,BoxGroup("Sources"),LabelText("音效播放器")]
        private AudioSource MusicSource;
        
        private SpawnPool audioSpawnPool;

        [BoxGroup("Snapshot"),ShowInInspector,ReadOnly,LabelText("快照列表")]
        private Dictionary<AudioSnapshotsType, AudioMixerSnapshot> snapshots = new Dictionary<AudioSnapshotsType, AudioMixerSnapshot>();

        /// <summary>快照过渡的通用时长。换 BGM、进出视频这类"场景级"切换用。</summary>
        private const float snapshotTimer = 1f;

        /// <summary>
        /// 人声的快照过渡时长，<b>比 <see cref="snapshotTimer"/> 短一个数量级</b>。
        ///
        /// 台词平均两三秒一句，用通用时长的话上一句的过渡还没走完下一句又重新开始，
        /// 实际效果是 BGM 一直在缓慢起伏，而且永远到不了"压低突出人声"的目标状态。
        /// </summary>
        private const float humanSnapshotTimer = 0.2f;

        /// <summary>
        /// <c>transitionTime</c> 的哨兵值：表示"按音频类型取默认过渡时长"。
        ///
        /// 需要它是因为人声和其它类型的合理默认值差一个数量级
        /// （见 <see cref="humanSnapshotTimer"/>），而参数默认值只能写死一个。
        /// 调用方显式传了非负数就用它的。
        /// </summary>
        private const float defaultTransition = -1f;

        /// <summary>按类型解析快照过渡时长。</summary>
        private static float ResolveTransition(AudioType audioType, float transitionTime)
        {
            if (transitionTime >= 0f)
            {
                return transitionTime;
            }

            return audioType == AudioType.Human ? humanSnapshotTimer : snapshotTimer;
        }

        /// <summary>
        /// 切到指定快照。快照表是策划在配置里填的，缺一条不该抛异常，静默跳过即可。
        /// </summary>
        private void TransitionSnapshot(AudioSnapshotsType type, float transitionTime)
        {
            if (snapshots.TryGetValue(type, out AudioMixerSnapshot snapshot) && snapshot != null)
            {
                snapshot.TransitionTo(transitionTime);
            }
        }

        /// <summary>
        /// 一条"会把混音器带走"的音轨的复位账本。
        ///
        /// 人声和视频播放时都会切到自己的快照（压低其它声音突出自己），但这是<b>临时</b>状态：
        /// 声一停就该回 <see cref="AudioSnapshotsType.Normal"/>，否则整个混音器就一直压着，
        /// 直到下次播 BGM 才被顺手带回来。
        ///
        /// BGM 不需要这本账（它的目标快照就是 Normal 本身），环境音和音效压根不碰快照。
        /// </summary>
        private sealed class SnapshotDuck
        {
            /// <summary>还欠一次"回 Normal"。</summary>
            public bool RestorePending;

            /// <summary>
            /// 是被暂停了，还是真的播完了。<c>AudioSource.isPlaying</c> 两种情况都返回 false，
            /// 光看它会把暂停当成播完 —— 一暂停其它声音就抬回来了。
            /// </summary>
            public bool Paused;

            /// <summary>回程时长。取的是来时那次的过渡时长，一去一回对称。</summary>
            public float RestoreTime;

            /// <summary>开播：切走了快照，记下欠一次回程。</summary>
            public void Duck(float transitionTime)
            {
                RestorePending = true;
                Paused = false;
                RestoreTime = transitionTime;
            }

            /// <summary>勾销欠账。收尾时快照表要清空，回程已经没有意义。</summary>
            public void Cancel()
            {
                RestorePending = false;
                Paused = false;
            }
        }

        private readonly SnapshotDuck humanDuck = new SnapshotDuck();
        private readonly SnapshotDuck videoDuck = new SnapshotDuck();

        /// <summary>
        /// 把播完的人声 / 视频轨带回 Normal 快照。
        ///
        /// 只能轮询：<c>AudioSource</c> 没有"播完了"的回调，而这两条轨都是 Play 出去就不管的
        /// （语音那边 <see cref="DramaAudio"/> 即发即忘，台词不等语音念完）。
        /// 每帧两个 bool 判断，代价可以忽略。
        /// </summary>
        private void Update()
        {
            RestoreIfSilent(humanDuck, humanSource);
            RestoreIfSilent(videoDuck, videoSource);
        }

        /// <summary>轨安静下来（播完 / 被停 / 轨没了）且不是暂停着，就把欠的回程走掉。</summary>
        private void RestoreIfSilent(SnapshotDuck duck, AudioSource source)
        {
            if (!duck.RestorePending)
            {
                return;
            }

            if (source != null && (source.isPlaying || duck.Paused))
            {
                return;
            }

            duck.RestorePending = false;
            TransitionSnapshot(AudioSnapshotsType.Normal, duck.RestoreTime);
        }

        [BoxGroup("混音器"),ShowInInspector,LabelText("混音器"),ReadOnly]
        private AudioMixer XMixer;
        
        #endregion
        
        #region Play

        /// <summary>
        /// 播放音频
        /// </summary>
        /// <param name="audioID">audio配置表ID</param>
        /// <param name="transitionTime">过度时间</param>
        public void PlayAudio(string audioID, float transitionTime = defaultTransition)
        {
            AudioItemData itemData = _audioConfiguration.GetDataByID(audioID);

            // 配置表里没有这个 ID 时 GetDataByID 返回 null，不拦就是一个空引用异常。
            // 报出 ID 比抛异常有用得多——调用方传的多半是策划填的字符串
            if (itemData == null)
            {
                Debug.LogError($"[Audio] 音频配置表里没有 ID「{audioID}」，本次播放已跳过");
                return;
            }

            transitionTime = ResolveTransition(itemData.audioType, transitionTime);

            switch (itemData.audioType)
            {
                case AudioType.BGM:
                    PlayBGM(itemData, transitionTime);
                    break;
                case AudioType.Ambient:
                    PlayAmbient(itemData);                    // 环境音不做快照过渡，见 PlayAmbient
                    break;
                case AudioType.Human:
                    PlayHuman(itemData,transitionTime);
                    break;
                case AudioType.Music:
                    // 音效不吃 transitionTime：一次性短音，不碰快照。音量走配置里的 InitVolume
                    PlayMusic(itemData);
                    break;
                case AudioType.Video:
                    PlayVideo(itemData,transitionTime);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        ///  播放音频
        /// </summary>
        /// <param name="audioPath"> 音频路径</param>
        /// <param name="audioType">音频类型</param>
        /// <param name="transitionTime">过度时间</param>
        public void PlayAudio(string audioPath,AudioType audioType,float transitionTime = defaultTransition)
        {
            if (string.IsNullOrEmpty(audioPath)) return;
            transitionTime = ResolveTransition(audioType, transitionTime);
            switch (audioType)
            {
                case AudioType.BGM:
                    PlayBGM(audioPath, transitionTime);
                    break;
                case AudioType.Ambient:
                    PlayAmbient(audioPath);
                    break;
                case AudioType.Human:
                    PlayHuman(audioPath, transitionTime);
                    break;
                case AudioType.Music:
                    // ★ 不能传 transitionTime —— 那个位置现在是【音量】。
                    //   传过去等于音量 = 3，直接爆音
                    PlayMusic(audioPath);
                    break;
                case AudioType.Video:
                    PlayVideo(audioPath, transitionTime);
                    break;
            }
        }
        
        /// <summary>
        ///  播放音频
        /// </summary>
        /// <param name="clip">音频Clip</param>
        /// <param name="audioType">音频类型</param>
        /// <param name="transitionTime">过度时间</param>
        public void PlayAudio(AudioClip clip,AudioType audioType,float transitionTime = defaultTransition)
        {
            if (clip == null) return;
            transitionTime = ResolveTransition(audioType, transitionTime);
            switch (audioType)
            {
                case AudioType.BGM:
                    PlayBGM(clip, transitionTime);
                    break;
                case AudioType.Ambient:
                    PlayAmbient(clip);
                    break;
                case AudioType.Human:
                    PlayHuman(clip, transitionTime);
                    break;
                case AudioType.Music:
                    // ★ 这里【不能】传 transitionTime：Music 那个重载的第二个参数是【音量】不是过渡时间
                    //   （PlayMusic(AudioClip, float volume, Action)）。原来传过去等于音量 = 3，直接爆音。
                    //   音效是一次性的、走对象池，本来也没有快照过渡这回事
                    PlayMusic(clip);
                    break;
                case AudioType.Video:
                    PlayVideo(clip, transitionTime);
                    break;
            }
        }

        /// <summary>停掉某一类音频。Music 会把当前所有在播的音效一起停掉。</summary>
        public void StopAudio(AudioType audioType)
        {
            switch (audioType)
            {
                case AudioType.BGM:     StopBGM(); break;
                case AudioType.Ambient: StopAmbient(); break;
                case AudioType.Human:   StopHuman(); break;
                case AudioType.Video:   StopVideo(); break;
                case AudioType.Music:   StopMusic(); break;
            }
        }

        /// <summary>
        /// 暂停某一类音频。<b>音效(Music)不支持</b> ——
        /// 它是一次性的短音，暂停一半再恢复没有意义，而且它的结束是靠计时器算的，
        /// 暂停会让计时和实际播放对不上。
        /// </summary>
        public void PauseAudio(AudioType audioType)
        {
            switch (audioType)
            {
                case AudioType.BGM:     PauseBGM(); break;
                case AudioType.Ambient: PauseAmbient(); break;
                // ★ 走 PauseHuman 而不是直接 humanSource.Pause()：那条路才会记住"是暂停不是播完"，
                //   直接操作 source 会让快照在暂停的瞬间就抬回 Normal
                case AudioType.Human:   PauseHuman(); break;
                case AudioType.Video:   PauseVideo(); break;
                case AudioType.Music:
                    Debug.LogWarning("[Audio] 音效(Music)是一次性短音，不支持暂停；要停就用 StopAudio");
                    break;
            }
        }

        /// <inheritdoc cref="PauseAudio"/>
        public void ResumeAudio(AudioType audioType)
        {
            switch (audioType)
            {
                case AudioType.BGM:     ResumeBGM(); break;
                case AudioType.Ambient: ResumeAmbient(); break;
                case AudioType.Human:   ResumeHuman(); break;
                case AudioType.Video:   ResumeVideo(); break;
                case AudioType.Music:
                    Debug.LogWarning("[Audio] 音效(Music)不支持暂停/恢复");
                    break;
            }
        }

        /// <summary>
        /// 全停。切场景、退出剧情、回主菜单这类"把声音清干净"的时刻用。
        ///
        /// 不主动摆快照 —— 混音器状态归调用方按新场景自己设。
        /// 唯一的例外是人声压低：那是 <see cref="PlayHuman(AudioClip,float)"/> 自己挖的坑，
        /// 人声一停就由 <see cref="Update"/> 自己填回 Normal，不留给调用方。
        /// </summary>
        public void StopAllAudio()
        {
            StopBGM();
            StopAmbient();
            StopHuman();
            StopVideo();
            StopMusic();
        }

        #endregion

        #region BGM
        
        /// <summary>
        /// 播放BGM
        /// </summary>
        /// <param name="itemData">配置数据</param>
        /// <param name="transitionTime">过度时间</param>
        private void PlayBGM(AudioItemData itemData, float transitionTime = snapshotTimer)
        {
            if (itemData == null) return;
            if (itemData.audioClip == null) return;
            PlayBGM(itemData.audioClip, transitionTime);
        }

        /// <summary>
        ///  播放BGM
        /// </summary>
        /// <param name="audioPath">音频路径</param>
        /// <param name="transitionTime">过度时间</param>
        public void PlayBGM(string audioPath, float transitionTime = snapshotTimer)
        {
            if (string.IsNullOrEmpty(audioPath)) return;
            AudioClip clip = AssetsManager.Instance.LoadAssets<AudioClip>(audioPath);
            if (clip == null) return;
            PlayBGM(clip, transitionTime);
        }
        
        /// <summary>
        ///  播放BGM
        /// </summary>
        /// <param name="clip">音频文件</param>
        /// <param name="transitionTime">过度时间</param>
        public void PlayBGM(AudioClip clip, float transitionTime = snapshotTimer)
        {
            if (clip == null) return;
            if (bgmSource.isActiveAndEnabled)
            {
                bgmSource.clip = clip;
                bgmSource.loop = true;
                bgmSource.Play();
            }
            TransitionSnapshot(AudioSnapshotsType.Normal, transitionTime);
        }

        /// <summary>
        /// 暂停BGM
        /// </summary>
        public void PauseBGM()
        {
            bgmSource?.Pause();
        }

        /// <summary>
        /// 恢复播放BGM
        /// </summary>
        public void ResumeBGM()
        {
            bgmSource?.UnPause();
        }

        /// <summary>
        /// 停止播放BGM音效
        /// </summary>
        public void StopBGM()
        {
            bgmSource?.Stop();
        }

        #endregion

        #region Ambient

        /// <summary>
        /// 播放环境音。
        ///
        /// <b>环境音不做快照过渡</b>，所以没有 transitionTime 参数：
        /// 它是垫在最底下的背景层，换一段环境音把整个混音器带一下反而不对
        /// （BGM、人声都会跟着起伏）。要压环境音音量走
        /// <see cref="SetAudioVolume"/> 的 AmbientItem 组。
        /// </summary>
        /// <param name="itemData"> 配置数据</param>
        private void PlayAmbient(AudioItemData itemData)
        {
            if (itemData == null) return;
            if (itemData.audioClip == null) return;
            PlayAmbient(itemData.audioClip);
        }

        /// <inheritdoc cref="PlayAmbient(AudioItemData)"/>
        /// <param name="audioPath">音频路径</param>
        private void PlayAmbient(string audioPath)
        {
            if (string.IsNullOrEmpty(audioPath)) return;
            AudioClip clip = AssetsManager.Instance.LoadAssets<AudioClip>(audioPath);
            if (clip == null) return;
            PlayAmbient(clip);
        }

        /// <inheritdoc cref="PlayAmbient(AudioItemData)"/>
        /// <param name="clip"> 音频Clip</param>
        private void PlayAmbient(AudioClip clip)
        {
            if (clip == null) return;
            if (ambientSource.isActiveAndEnabled)
            {
                ambientSource.clip = clip;
                ambientSource.loop = true;
                ambientSource.Play();
            }
        }

        /// <summary>
        /// 停止播放环境音
        /// </summary>
        public void StopAmbient()
        {
            ambientSource?.Stop();
        }

        /// <summary>暂停环境音</summary>
        public void PauseAmbient()
        {
            ambientSource?.Pause();
        }

        /// <summary>恢复播放环境音</summary>
        public void ResumeAmbient()
        {
            ambientSource?.UnPause();
        }

        #endregion
        
        #region Human

        /// <summary>
        ///  播放人声
        /// </summary>
        /// <param name="itemData">配置数据</param>
        /// <param name="transitionTime">过度时间</param>
        private void PlayHuman(AudioItemData itemData, float transitionTime = humanSnapshotTimer)
        {
            if (itemData == null) return;
            if (itemData.audioClip == null) return;
            PlayHuman(itemData.audioClip, transitionTime);
        }
        
        /// <summary>
        ///  播放人声
        /// </summary>
        /// <param name="audioPath">音频路径</param>
        /// <param name="transitionTime">过度时间</param>
        private void PlayHuman(string audioPath, float transitionTime = humanSnapshotTimer)
        {
            if (string.IsNullOrEmpty(audioPath)) return;
            AudioClip clip = AssetsManager.Instance.LoadAssets<AudioClip>(audioPath);
            if (clip == null) return;
            PlayHuman(clip, transitionTime);
        }
        
        /// <summary>
        /// 播放人声
        /// </summary>
        /// <param name="clip"> 音频Clip</param>
        /// <param name="transitionTime"> 过度时间</param>
        private void PlayHuman(AudioClip clip, float transitionTime = humanSnapshotTimer)
        {
            if (clip == null) return;
            if (humanSource.isActiveAndEnabled)
            {
                humanSource.clip = clip;
                humanSource.Play();
            }

            TransitionSnapshot(AudioSnapshotsType.Human, transitionTime);

            // 压低其它声音只该持续到这句念完为止，之后由 Update 带回 Normal
            humanDuck.Duck(transitionTime);
        }

        /// <summary>
        /// 停人声。快照不在这里切 —— 轨一静下来 <see cref="Update"/> 就会回 Normal，
        /// 晚一帧但少一次"这边刚切回去、下一句马上又切过来"的来回拉扯。
        /// </summary>
        public void StopHuman()
        {
            humanSource?.Stop();
            humanDuck.Paused = false;
        }

        /// <summary>暂停人声</summary>
        public void PauseHuman()
        {
            humanSource?.Pause();

            // 暂停不算念完，快照要一直压着，等恢复继续念
            humanDuck.Paused = true;
        }

        /// <summary>恢复播放人声</summary>
        public void ResumeHuman()
        {
            humanSource?.UnPause();
            humanDuck.Paused = false;
        }

        /// <summary>人声轨是不是还在播。剧情的"自动播放"要等语音念完才翻页，靠它判断。</summary>
        public bool IsHumanPlaying => humanSource != null && humanSource.isPlaying;
        #endregion

        #region Video
        
        /// <summary>
        /// 播放视频
        /// </summary>
        /// <param name="itemData"> 配置数据</param>
        /// <param name="transitionTime"> 过度时间</param>
        private void PlayVideo(AudioItemData itemData, float transitionTime = snapshotTimer)
        {
            if (itemData == null) return;
            if (itemData.audioClip == null) return;
            PlayVideo(itemData.audioClip, transitionTime);
        }
        
        /// <summary>
        /// 播放视频
        /// </summary>
        /// <param name="audioPath">音频路径</param>
        /// <param name="transitionTime">过度时间</param>
        private void PlayVideo(string audioPath, float transitionTime = snapshotTimer)
        {
            if (string.IsNullOrEmpty(audioPath)) return;
            AudioClip clip = AssetsManager.Instance.LoadAssets<AudioClip>(audioPath);
            if (clip == null) return;
            PlayVideo(clip, transitionTime);
        }
        
        /// <summary>
        /// 播放视频
        /// </summary>
        /// <param name="clip">音频文件</param>
        /// <param name="transitionTime"> 过度时间</param>
        private void PlayVideo(AudioClip clip, float transitionTime = snapshotTimer)
        {
            if (clip == null) return;
            if (videoSource.isActiveAndEnabled)
            {
                videoSource.clip = clip;
                videoSource.Play();
            }

            TransitionSnapshot(AudioSnapshotsType.Video, transitionTime);

            // 和人声同理：视频音轨压着别的声音只该持续到这段视频放完
            videoDuck.Duck(transitionTime);
        }

        /// <inheritdoc cref="StopHuman"/>
        public void StopVideo()
        {
            videoSource?.Stop();
            videoDuck.Paused = false;
        }

        /// <summary>暂停视频音轨</summary>
        public void PauseVideo()
        {
            videoSource?.Pause();
            videoDuck.Paused = true;
        }

        /// <summary>恢复播放视频音轨</summary>
        public void ResumeVideo()
        {
            videoSource?.UnPause();
            videoDuck.Paused = false;
        }

        #endregion

        #region Music
        
        /// <summary>
        /// 按配置播一条音效。
        ///
        /// <b>没有 transitionTime</b>：音效是一次性短音，不碰混音器快照。
        /// 原来那个参数从头到尾没被读过，纯摆设。
        ///
        /// <b>走 <see cref="MusicItemSource.PlayMusic(AudioItemData)"/> 而不是取出 clip 再播</b>：
        /// 配置里的 <c>InitVolume</c> 和音高随机范围（soundPitchMin/Max）只有那条路会用，
        /// 取 clip 转走等于把策划配的音量和音高随机全丢了。
        /// </summary>
        private void PlayMusic(AudioItemData itemData)
        {
            if (itemData == null) return;
            if (itemData.audioClip == null) return;

            Transform temp = audioSpawnPool.Spawn(MusicSource.transform);
            MusicItemSource musicItemSource = temp.GetComponent<MusicItemSource>();
            musicItemSource.PlayMusic(itemData);
        }

        /// <summary>
        /// 按路径播一条音效。没有配置可依，音量由调用方给。
        /// </summary>
        /// <param name="audioPath">音频的AA包地址</param>
        /// <param name="volume">音量倍率，1 = 原始音量。<b>原来这个位置是 transitionTime，但音效不做快照过渡</b>。</param>
        private void PlayMusic(string audioPath,float volume = 1f)
        {
            if (string.IsNullOrEmpty(audioPath)) return;
            AudioClip clip = AssetsManager.Instance.LoadAssets<AudioClip>(audioPath);
            if (clip == null) return;
            PlayMusic(clip, volume);
        }
        
        public void PlayMusic(AudioClip clip,float volume = 1f,Action OnMusicPlayEnd = null)
        {
            if (clip == null) return;
            Transform temp = audioSpawnPool.Spawn(MusicSource.transform);
            MusicItemSource  musicItemSource = temp.GetComponent<MusicItemSource>();
            musicItemSource.PlayMusic(clip,volume, OnMusicPlayEnd);
        }

        /// <summary>
        /// 停掉当前所有在播的音效。
        ///
        /// 音效走对象池、可能同时有好几条，所以没有"停某一条"这回事 ——
        /// 要精确控制单条，用 <see cref="PlayMusic(AudioClip,float,Action)"/> 的结束回调自己管。
        ///
        /// <b>必须先拷一份再遍历</b>：Stop 内部会把实例还回池子，
        /// 而池子本身就是那个列表，边走边改会漏掉一半。
        /// </summary>
        public void StopMusic()
        {
            if (audioSpawnPool == null)
            {
                return;
            }

            List<Transform> playing = new List<Transform>(audioSpawnPool);

            for (int i = 0; i < playing.Count; i++)
            {
                if (playing[i] == null)
                {
                    continue;
                }

                MusicItemSource item = playing[i].GetComponent<MusicItemSource>();
                if (item != null)
                {
                    item.Stop();
                }
            }
        }

        #endregion

        #region Functions

        /// <summary>
        /// 设置音频音量
        /// </summary>
        /// <param name="audioType">音频组类型</param>
        /// <param name="volume">音频组值</param>
        public void SetAudioVolume(AudioMixerGroupType audioType, float volume)
        {
            switch (audioType)
            {
                case AudioMixerGroupType.Master:
                    PlayerPrefs.SetFloat("MasterVolume", volume);
                    SetMixerVolume("MasterVolume", volume);
                    break;
                case AudioMixerGroupType.AmbientMaster:
                    PlayerPrefs.SetFloat("AmbientMasterVolume", volume);
                    SetMixerVolume("AmbientMasterVolume", volume);
                    break;
                case AudioMixerGroupType.AmbientItem:
                    PlayerPrefs.SetFloat("AmbientItemVolume", volume);
                    SetMixerVolume("AmbientItemVolume", volume);
                    break;
                case AudioMixerGroupType.BGMMaster:
                    PlayerPrefs.SetFloat("BGMasterVolume", volume);
                    SetMixerVolume("BGMasterVolume", volume);
                    break;
                case AudioMixerGroupType.BGMItem:
                    PlayerPrefs.SetFloat("BGMItemVolume", volume);
                    SetMixerVolume("BGMItemVolume", volume);
                    break;
                case AudioMixerGroupType.MusicMaster:
                    PlayerPrefs.SetFloat("MusicMasterVolume", volume);
                    SetMixerVolume("MusicMasterVolume", volume);
                    break;
                case AudioMixerGroupType.MusicItem:
                    PlayerPrefs.SetFloat("MusicItemVolume", volume);
                    SetMixerVolume("MusicItemVolume", volume);
                    break;
                case AudioMixerGroupType.HumanMaster:
                    PlayerPrefs.SetFloat("HumanMasterVolume", volume);
                    SetMixerVolume("HumanMasterVolume", volume);
                    break;
                case AudioMixerGroupType.HumanItem:
                    PlayerPrefs.SetFloat("HumanItemVolume", volume);
                    SetMixerVolume("HumanItemVolume", volume);
                    break;
                case AudioMixerGroupType.VideoMaster:
                    PlayerPrefs.SetFloat("VideoMasterVolume", volume);
                    SetMixerVolume("VideoMasterVolume", volume);
                    break;
                case AudioMixerGroupType.VideoItem:
                    PlayerPrefs.SetFloat("VideoItemVolume", volume);
                    SetMixerVolume("VideoItemVolume", volume);
                    break;
                default:
                    break;
            }
        }
        
        /// <summary>
        /// 设置混合器音量
        /// </summary>
        private void SetMixerVolume(string mixerName, float value)
        {
            XMixer.SetFloat(mixerName, ConvertMixerVolume(value));
        }
        
        /// <summary>
        /// 混合器音量下限(dB),等同于静音
        /// </summary>
        private const float MinVolumeDB = -80f;

        /// <summary>
        /// 滑条中点(0.5)对应的振幅倍数,即 0dB 的基准点
        /// </summary>
        private const float NormalizedVolumeAnchor = 0.5f;

        /// <summary>
        /// 将0~1的滑条值转换为混合器音阶值(dB)
        /// 0 -> -80dB(静音), 0.25 -> -6dB, 0.5 -> 0dB(默认), 1 -> +6dB
        /// </summary>
        /// <param name="amount">0~1的滑条值</param>
        /// <returns>混合器音阶值(dB)</returns>
        private float ConvertMixerVolume(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (amount <= 0f) return MinVolumeDB;
            // 以振幅比取对数,保证滑条中点为 0dB,听感变化均匀
            return Mathf.Max(MinVolumeDB, 20f * Mathf.Log10(amount / NormalizedVolumeAnchor));
        }

        #endregion

        #region Tools

        /// <summary>
        /// 获取对应类型的混合器组件
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public AudioMixerGroup GetTypeMixerGroup(AudioMixerGroupType type)
        {
            return _audioConfiguration.GetByMixerGroup(type);
        }

        #endregion
        
    }
}

