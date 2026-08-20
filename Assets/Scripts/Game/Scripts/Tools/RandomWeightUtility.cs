using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    public static class RandomWeightUtility
    {
        /// <summary>
        /// 加权随机
        /// </summary>
        /// <typeparam name="T">随机对象类型</typeparam>
        /// <param name="list">随机列表</param>
        /// <param name="getWeight">获取权重的方法</param>
        /// <returns>随机出来的对象</returns>
        public static T GetRandomByWeight<T>(IList<T> list, Func<T, int> getWeight)
        {
            return GetRandomByWeight(list, getWeight, null);
        }

        /// <summary>
        /// 加权随机，可通过过滤条件排除不参与随机的对象。
        /// </summary>
        /// <typeparam name="T">随机对象类型</typeparam>
        /// <param name="list">随机列表</param>
        /// <param name="getWeight">获取权重的方法</param>
        /// <param name="canRandom">是否允许参与随机。返回 false 的对象会被排除，例如已解锁物品不再参与随机。</param>
        /// <returns>随机出来的对象</returns>
        public static T GetRandomByWeight<T>(IList<T> list, Func<T, int> getWeight, Func<T, bool> canRandom)
        {
            if (list == null || list.Count == 0)
            {
                Debug.LogError("加权随机失败：列表为空");
                return default;
            }

            if (getWeight == null)
            {
                Debug.LogError("加权随机失败：getWeight 为空");
                return default;
            }

            int totalWeight = 0;

            for (int i = 0; i < list.Count; i++)
            {
                if (canRandom != null && !canRandom(list[i]))
                {
                    continue;
                }

                int weight = getWeight(list[i]);

                if (weight > 0)
                {
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 0)
            {
                Debug.LogError("加权随机失败：总权重小于等于 0");
                return default;
            }

            int randomValue = UnityEngine.Random.Range(0, totalWeight);

            int currentWeight = 0;

            for (int i = 0; i < list.Count; i++)
            {
                if (canRandom != null && !canRandom(list[i]))
                {
                    continue;
                }

                int weight = getWeight(list[i]);

                if (weight <= 0)
                {
                    continue;
                }

                currentWeight += weight;

                if (randomValue < currentWeight)
                {
                    return list[i];
                }
            }

            return default;
        }

        /// <summary>
        /// 加权随机，可按 Key 排除一批对象。
        /// 适合“已解锁物品 ID 不再随机”这类场景。
        /// </summary>
        /// <typeparam name="T">随机对象类型</typeparam>
        /// <typeparam name="TKey">排除 Key 类型，例如 long 物品 ID</typeparam>
        /// <param name="list">随机列表</param>
        /// <param name="getWeight">获取权重的方法</param>
        /// <param name="getKey">获取对象 Key 的方法</param>
        /// <param name="excludeKeys">需要排除的 Key 列表，例如已解锁物品 ID</param>
        /// <returns>随机出来的对象</returns>
        public static T GetRandomByWeight<T, TKey>(
            IList<T> list,
            Func<T, int> getWeight,
            Func<T, TKey> getKey,
            IEnumerable<TKey> excludeKeys)
        {
            HashSet<TKey> excludeSet = BuildExcludeSet(excludeKeys);
            return GetRandomByWeight(list, getWeight, item => !excludeSet.Contains(getKey(item)));
        }

        /// <summary>
        /// 加权随机抽取多个对象，每次抽中后会从本轮候选中移除，保证本次返回结果不重复。
        /// </summary>
        /// <typeparam name="T">随机对象类型</typeparam>
        /// <param name="list">随机列表</param>
        /// <param name="count">需要抽取的数量</param>
        /// <param name="getWeight">获取权重的方法</param>
        /// <param name="canRandom">是否允许参与随机。返回 false 的对象会被排除。</param>
        /// <returns>本次抽中的不重复对象列表。候选不足时返回能抽到的全部对象。</returns>
        public static List<T> GetRandomListByWeightNoRepeat<T>(
            IList<T> list,
            int count,
            Func<T, int> getWeight,
            Func<T, bool> canRandom = null)
        {
            List<T> result = new();

            if (list == null || list.Count == 0 || count <= 0)
            {
                return result;
            }

            if (getWeight == null)
            {
                Debug.LogError("加权随机失败：getWeight 为空");
                return result;
            }

            List<T> candidates = new();
            for (int i = 0; i < list.Count; i++)
            {
                T item = list[i];
                if (canRandom != null && !canRandom(item))
                {
                    continue;
                }

                if (getWeight(item) > 0)
                {
                    candidates.Add(item);
                }
            }

            while (result.Count < count && candidates.Count > 0)
            {
                int selectedIndex = GetRandomIndexByWeight(candidates, getWeight);
                if (selectedIndex < 0)
                {
                    break;
                }

                result.Add(candidates[selectedIndex]);
                candidates.RemoveAt(selectedIndex);
            }

            return result;
        }

        /// <summary>
        /// 加权随机抽取多个对象，每次抽中后会从本轮候选中移除，并按 Key 排除一批对象。
        /// </summary>
        /// <typeparam name="T">随机对象类型</typeparam>
        /// <typeparam name="TKey">排除 Key 类型，例如 long 物品 ID</typeparam>
        /// <param name="list">随机列表</param>
        /// <param name="count">需要抽取的数量</param>
        /// <param name="getWeight">获取权重的方法</param>
        /// <param name="getKey">获取对象 Key 的方法</param>
        /// <param name="excludeKeys">需要排除的 Key 列表，例如已解锁物品 ID</param>
        /// <returns>本次抽中的不重复对象列表。候选不足时返回能抽到的全部对象。</returns>
        public static List<T> GetRandomListByWeightNoRepeat<T, TKey>(
            IList<T> list,
            int count,
            Func<T, int> getWeight,
            Func<T, TKey> getKey,
            IEnumerable<TKey> excludeKeys)
        {
            HashSet<TKey> excludeSet = BuildExcludeSet(excludeKeys);
            return GetRandomListByWeightNoRepeat(list, count, getWeight, item => !excludeSet.Contains(getKey(item)));
        }

        /// <summary>
        /// 按权重从候选列表中随机一个索引。
        /// </summary>
        private static int GetRandomIndexByWeight<T>(IList<T> list, Func<T, int> getWeight)
        {
            int totalWeight = 0;

            for (int i = 0; i < list.Count; i++)
            {
                int weight = getWeight(list[i]);
                if (weight > 0)
                {
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 0)
            {
                return -1;
            }

            int randomValue = UnityEngine.Random.Range(0, totalWeight);
            int currentWeight = 0;

            for (int i = 0; i < list.Count; i++)
            {
                int weight = getWeight(list[i]);
                if (weight <= 0)
                {
                    continue;
                }

                currentWeight += weight;
                if (randomValue < currentWeight)
                {
                    return i;
                }
            }

            return -1;
        }

        private static HashSet<TKey> BuildExcludeSet<TKey>(IEnumerable<TKey> excludeKeys)
        {
            return excludeKeys == null ? new HashSet<TKey>() : new HashSet<TKey>(excludeKeys);
        }
    }
}
