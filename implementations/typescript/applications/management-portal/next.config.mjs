/** The owned Node host serves all authenticated reads dynamically. */
const config = {
  poweredByHeader: false,
  serverExternalPackages: ["pg", "next-auth", "@auth/pg-adapter"],
  async headers() {
    return [{ source: "/:path*", headers: [
      { key: "X-Content-Type-Options", value: "nosniff" },
      { key: "Referrer-Policy", value: "no-referrer" },
      { key: "X-Frame-Options", value: "DENY" },
      { key: "Content-Security-Policy", value: "base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'" }
    ] }];
  }
};
export default config;
