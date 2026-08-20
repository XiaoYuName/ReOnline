using System;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;

namespace Assets.Scripts.Utils
{
	/// <summary>
	/// 基于 UnityEngine.Random 的随机数辅助工具，提供随机布尔值、随机元素、权重抽取、
	/// 离散数值、集合乱序和无放回抽取等功能。
	/// </summary>
	public static class RandomUtil
	{
		/// <summary>
		/// 以相同概率随机返回 true 或 false。
		/// </summary>
		/// <returns>true 和 false 的出现概率各为 50%。</returns>
		public static bool NextBool()
		{
			return Random.Range(0, 2) == 0;
		}

		/// <summary>
		/// 从指定数组中等概率随机返回一个元素。
		/// </summary>
		/// <typeparam name="T">数组元素类型。</typeparam>
		/// <param name="array">用于随机抽取的数组，不能为 null 或空数组。</param>
		/// <returns>数组中的一个随机元素。</returns>
		public static T NextItem<T>(T[] array)
		{
			return array[Random.Range(0, array.Length)];
		}

		/// <summary>
		/// 从指定列表中等概率随机返回一个元素。
		/// </summary>
		/// <typeparam name="T">列表元素类型。</typeparam>
		/// <param name="list">用于随机抽取的列表，不能为 null 或空列表。</param>
		/// <returns>列表中的一个随机元素。</returns>
		public static T NextItem<T>(List<T> list)
		{
			return list[Random.Range(0, list.Count)];
		}

        /// <summary>
        /// 从指定枚举类型的全部枚举值中等概率随机返回一个值。
        /// </summary>
		/// <typeparam name="T">枚举类型；调用时必须传入 enum 类型。</typeparam>
		/// <returns>枚举 T 中的一个随机枚举值。</returns>
		public static T NextEnum<T>()
		{
			var values = Enum.GetValues(typeof(T));
			return (T)values.GetValue(Random.Range(0, values.Length));
		}

        /// <summary>
        /// 根据整数权重随机选择并返回权重数组的下标。
        /// 权重越大的下标被选中的概率越高，例如 [1, 3] 对应约 25% 和 75% 的概率。
        /// </summary>
		/// <param name="weights">
		/// 每个下标对应的整数权重。数组不能为 null 或空，权重应为非负数，并且权重总和应大于 0。
		/// </param>
		/// <returns>根据权重随机选中的数组下标，范围为 0 到 weights.Length - 1。</returns>
		public static int NextWeightedInd(int[] weights)
		{
			return NextWeightedInd(weights.Select(i => (float)i).ToArray());
		}

        /// <summary>
        /// 根据浮点数权重随机选择并返回权重数组的下标。
        /// 每个下标的选中概率约等于该下标权重除以全部权重之和。
        /// </summary>
		/// <param name="weights">
		/// 每个下标对应的浮点数权重。数组不能为 null 或空，权重应为非负数，并且权重总和应大于 0。
		/// </param>
		/// <returns>根据权重随机选中的数组下标，范围为 0 到 weights.Length - 1。</returns>
		/// <exception cref="Exception">权重数据无效或计算后未能选中任何下标时抛出。</exception>
		public static int NextWeightedInd(float[] weights)
		{
			float random = Random.Range(0f, weights.Sum());
			float sum = 0f;
			for (int i = 0; i < weights.Length; i++)
			{
				sum += weights[i];
				if (random <= sum)
					return i;
			}
			throw new Exception("Logic error!");
		}

		/// <summary>
		/// 在最小值和最大值之间生成一个等间距的离散随机值。
		/// 当 count 大于等于 2 时，候选值包含 min、max 以及两者之间的等分点。
		/// </summary>
		/// <param name="min">候选范围的最小值。</param>
		/// <param name="max">候选范围的最大值。</param>
		/// <param name="count">离散候选值数量；小于 2 时直接返回 min。</param>
		/// <returns>从 count 个等间距候选值中随机选中的一个值。</returns>
		public static float NextDiscrete(float min, float max, int count)
		{
			if (count < 2) return min;
			return min + Random.Range(0, count) * (max - min) / (count - 1);
		}

        /// <summary>
        /// 使用 Fisher-Yates 算法随机打乱列表中的元素顺序。
        /// 此方法会直接修改传入列表，不会创建并返回新列表。
        /// </summary>
		/// <typeparam name="T">列表元素类型。</typeparam>
		/// <param name="list">需要原地随机打乱的列表，不能为 null。</param>
		public static void Shuffle<T>(this List<T> list)
		{
			T[] array = list.ToArray();
			ShuffleArray(array);
			list.Clear();
			list.AddRange(array);
		}

        /// <summary>
        /// 使用 Fisher-Yates 算法随机打乱数组中的元素顺序。
        /// 此方法会直接修改传入数组，不会创建并返回新数组。
        /// </summary>
		/// <typeparam name="T">数组元素类型。</typeparam>
		/// <param name="array">需要原地随机打乱的数组，不能为 null。</param>
		public static void ShuffleArray<T>(T[] array)
		{
			for (int i = 1; i < array.Length; i++)
			{
				int indRnd = Random.Range(0, i + 1);
                T temp = array[i];
				array[i] = array[indRnd];
				array[indRnd] = temp;
			}
		}

		/// <summary>
		/// 从指定列表中无放回地随机抽取若干元素，并返回包含抽取结果的新列表。
		/// 同一个列表下标不会被重复抽取；如果源列表本身包含相同值，结果中仍可能出现相同值。
		/// </summary>
		/// <typeparam name="T">列表元素类型。</typeparam>
		/// <param name="list">用于随机抽取的源列表，不能为 null。</param>
		/// <param name="count">需要抽取的元素数量，有效范围为 0 到 list.Count。</param>
		/// <returns>包含 count 个随机元素的新列表，源列表不会被修改。</returns>
		public static List<T> Take<T>(List<T> list, int count)
		{
			List<T> items = new List<T>();
			List<int> remainedIndexes = Enumerable.Range(0, list.Count).ToList();
			for (int i = 0; i < count; i++)
			{
				int selectedIndex = NextItem(remainedIndexes);
				remainedIndexes.Remove(selectedIndex);
				items.Add(list[selectedIndex]);
			}
			return items;
		}

        /// <summary>
        /// 从两个候选值中等概率随机返回一个值。
        /// </summary>
		/// <typeparam name="T">候选值类型。</typeparam>
		/// <param name="item1">第一个候选值。</param>
		/// <param name="item2">第二个候选值。</param>
		/// <returns>item1 或 item2，两个值的选中概率各为 50%。</returns>
        public static T Next<T>(T item1, T item2)
        {
            return NextBool() ? item1 : item2;
        }

        /// <summary>
        /// 从三个候选值中等概率随机返回一个值。
        /// </summary>
		/// <typeparam name="T">候选值类型。</typeparam>
		/// <param name="item1">第一个候选值。</param>
		/// <param name="item2">第二个候选值。</param>
		/// <param name="item3">第三个候选值。</param>
		/// <returns>item1、item2 或 item3，每个值的选中概率均约为三分之一。</returns>
        public static T Next<T>(T item1, T item2, T item3)
        {
            int n = Random.Range(0, 3);
            return n == 0 ? item1 : (n == 1 ? item2 : item3);
        }

	}
}
