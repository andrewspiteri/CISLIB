/** The owned Node host serves all authenticated reads dynamically. */
const config = {
  poweredByHeader: false,
  compress: false,
  serverExternalPackages: ["pg", "next-auth", "@auth/pg-adapter"],
  async headers() {
    return [{ source: "/:path*", headers: [
      { key: "X-Content-Type-Options", value: "nosniff" },
      { key: "Referrer-Policy", value: "same-origin" },
      { key: "X-Frame-Options", value: "DENY" }
    ] }];
  }
};
export default config;
