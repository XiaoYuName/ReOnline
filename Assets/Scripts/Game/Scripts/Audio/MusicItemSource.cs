using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PathologicalGames;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace XFramework
{
    public class MusicItemSource : GameBase
    {
        private AudioSource musicSource;

        private CancellationTokenSource _tokenSource;
        [ReadOnly,LabelText("播放结束回调")]
        public Action OnMusicPlayEnd;
        private void Awake()
        {
            musicSource = GetComponent<AudioSource>();
            musicSource.outputAudioMixerGroup = AudioManager.Instance.
                GetTypeMixerGroup(AudioMixerGroupType.MusicItem);
        }
        
        public void PlayMusic(AudioItemData audioItemData)
        {
            if (audioItemData == null)
            {
                OnMusicPlayEnd?.Invoke();
                return;
            }
            if (audioItemData.audioClip == null)
            {
                OnMusicPlayEnd?.Invoke();
                return;
            }
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                _tokenSource = null;
            }
            _tokenSource = new CancellationTokenSource();
            musicSource.clip = audioItemData.audioClip;
            musicSource.volume = audioItemData.InitVolume;
            musicSource.pitch = UnityEngine.Random.Range(audioItemData.soundPitchMin, audioItemData.soundPitchMax);
            musicSource.Play();
            WaitMusic().Forget();
        }

        public void PlayMusic(AudioClip clip, float volume = 1f,Action OnMusicPlayEnd = null)
        {
            this.OnMusicPlayEnd = OnMusicPlayEnd;
            if (clip == null)
            {
                OnMusicPlayEnd?.Invoke();
                return;
            }
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                _tokenSource = null;
            }
            _tokenSource = new CancellationTokenSource();
            musicSource.clip = clip;
            musicSource.volume = volume;
            musicSource.Play();
            WaitMusic().Forget();
        }

        private async UniTask WaitMusic()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(musicSource.clip.length),cancellationToken: _tokenSource.Token);
            Finish();
        }

        /// <summary>
        /// 提前掐掉这一条音效。<see cref="AudioManager.StopMusic"/> 用。
        ///
        /// <b>结束回调照样触发</b>：调用方可能在 await 它，被强制停止也是一种"结束"，
        /// 不触发的话那边就永远挂着了。
        /// </summary>
        public void Stop()
        {
            if (_tokenSource == null)
            {
                return;   // 没在播，或者已经自然结束回池了
            }

            // 先掐计时器，否则 Finish 里回了池、时间到了那条又会把已经复用的实例再收一次
            _tokenSource.Cancel();
            _tokenSource = null;

            Finish();
        }

        /// <summary>收尾：复位播放器、触发回调、还回对象池。自然播完和被掐都走这里。</summary>
        private void Finish()
        {
            _tokenSource = null;

            OnMusicPlayEnd?.Invoke();
            OnMusicPlayEnd = null;   // 实例是复用的，不清掉下一条音效会连着上一条的回调一起触发

            musicSource.Stop();
            musicSource.clip = null;
            musicSource.volume = 0.5f;
            musicSource.pitch = 1f;

            PoolManager.Pools["AudioManager"].Despawn(transform);
        }
    }
}

