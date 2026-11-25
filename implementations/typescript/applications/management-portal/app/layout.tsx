import type { ReactNode } from "react";
import "./globals.css";
export const metadata = { title: "CIS Management", robots: { index: false, follow: false } };
export default function RootLayout({ children }: { children: ReactNode }) {
  return <html lang="en"><body><a className="skip-link" href="#main-content">Skip to content</a>{children}</body></html>;
}
