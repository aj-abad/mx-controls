fn main() {
    // The portable build carries the agent inside the app; see `embedded_agent`.
    if std::env::var_os("CARGO_FEATURE_EMBEDDED_AGENT").is_some() {
        let target = std::env::var("TARGET").unwrap();
        let extension = if target.contains("windows") { ".exe" } else { "" };
        let agent = std::path::Path::new(&std::env::var("CARGO_MANIFEST_DIR").unwrap())
            .join("binaries")
            .join(format!("mx-agent-{target}{extension}"));
        println!("cargo:rerun-if-changed={}", agent.display());
        println!("cargo:rustc-env=MX_EMBEDDED_AGENT={}", agent.display());
    }
    tauri_build::build()
}
