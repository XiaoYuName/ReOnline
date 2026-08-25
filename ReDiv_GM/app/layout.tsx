import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'ReDiv GM 控制台',
  description: 'ReDiv 本地服务器运维与玩家数据管理工具',
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="zh-CN"><body>{children}</body></html>;
}
