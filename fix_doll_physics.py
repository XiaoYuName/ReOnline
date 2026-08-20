#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
批量修复娃娃预制体的物理参数，使其与原作一致
"""
import os
import re

def fix_rigidbody_params(file_path):
    """修复单个预制体文件的Rigidbody2D参数"""
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # 检查是否包含 Rigidbody2D
    if 'Rigidbody2D:' not in content:
        return False

    # 替换物理参数（参考原作：mass=0.5, linearDamping=0, angularDamping=0.05, gravityScale=0.5）
    # 你的预制体：mass=0.1, linearDamping=2, angularDamping=8, gravityScale=1

    # 修复 Mass
    content = re.sub(r'm_Mass: 0\.1\b', 'm_Mass: 0.5', content)

    # 修复 LinearDamping
    content = re.sub(r'm_LinearDamping: 2\b', 'm_LinearDamping: 0', content)

    # 修复 AngularDamping
    content = re.sub(r'm_AngularDamping: 8\b', 'm_AngularDamping: 0.05', content)

    # 修复 GravityScale
    content = re.sub(r'm_GravityScale: 1\b', 'm_GravityScale: 0.5', content)

    # 确保 Interpolate 开启
    content = re.sub(r'm_Interpolate: 0\b', 'm_Interpolate: 1', content)

    # 写回文件
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)

    return True

def main():
    doll_dir = r"D:\GitLabProject\AFramework\Assets\AddressableAssets\Remote\Prefabs\Doll"

    if not os.path.exists(doll_dir):
        print(f"目录不存在: {doll_dir}")
        return

    fixed_count = 0
    for filename in os.listdir(doll_dir):
        if filename.startswith("DollController_") and filename.endswith(".prefab"):
            file_path = os.path.join(doll_dir, filename)
            if fix_rigidbody_params(file_path):
                fixed_count += 1
                print(f"✓ 修复: {filename}")

    print(f"\n总计修复 {fixed_count} 个娃娃预制体")
    print("\n修改内容:")
    print("  m_Mass: 0.1 → 0.5")
    print("  m_LinearDamping: 2 → 0")
    print("  m_AngularDamping: 8 → 0.05")
    print("  m_GravityScale: 1 → 0.5")
    print("  m_Interpolate: 0 → 1")

if __name__ == "__main__":
    main()
