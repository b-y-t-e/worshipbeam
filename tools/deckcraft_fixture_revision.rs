/// The pinned DeckCraft revision shared by all DeckCraft dependencies in a fixture crate.
pub fn upstream_rev(manifest: &str) -> &str {
    manifest
        .lines()
        .find_map(|line| line.strip_prefix("deckcraft-anim ="))
        .and_then(|dependency| dependency.split_once("rev = \""))
        .and_then(|(_, pin)| pin.split_once('"'))
        .map(|(revision, _)| revision)
        .expect("deckcraft-anim must pin a revision in Cargo.toml")
}
