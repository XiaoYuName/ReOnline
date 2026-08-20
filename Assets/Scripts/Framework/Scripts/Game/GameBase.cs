using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    public abstract class GameBase : SerializedMonoBehaviour
    {
        /// <summary>
        /// 获取子物体对象
        /// </summary>
        /// <param name="path">路径</param>
        /// <returns></returns>
        protected GameObject Get(string path)
        {
            return FindTransform(path).gameObject;
        }

        /// <summary>
        /// 获取自身子物体组件
        /// </summary>
        /// <param name="path">路径</param>
        /// <typeparam name="T">组件</typeparam>
        /// <returns></returns>
        protected T Get<T>(string path) where T: Component
        {
            return FindTransform(path).GetComponent<T>();
        }

        /// <summary>
        /// 根据相对路径查找物体。
        /// 支持使用 "." 表示当前层级，使用 ".." 表示父层级。
        /// 例如："../XX/XX" 表示先移动到当前物体的父物体，再依次查找两个子物体。
        /// </summary>
        /// <param name="path">相对于当前物体的层级路径</param>
        /// <returns>路径对应的 Transform</returns>
        private Transform FindTransform(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return transform;
            }

            Transform current = transform;
            string normalizedPath = path.Replace('\\', '/');
            string[] pathNodes = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (string pathNode in pathNodes)
            {
                if (pathNode == ".")
                {
                    continue;
                }

                if (pathNode == "..")
                {
                    if (current.parent == null)
                    {
                        throw CreatePathNotFoundException(path, pathNode);
                    }

                    current = current.parent;
                    continue;
                }

                Transform child = current.Find(pathNode);
                if (child == null)
                {
                    throw CreatePathNotFoundException(path, pathNode);
                }

                current = child;
            }

            return current;
        }

        private InvalidOperationException CreatePathNotFoundException(string path, string pathNode)
        {
            string message = $"Path: {path} 路径不存在，无法解析节点: {pathNode}，起始物体: {transform.name}";
            Debug.LogError(message, this);
            return new InvalidOperationException(message);
        }
    }
}

