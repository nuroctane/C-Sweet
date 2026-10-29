// Both layouts and portalled menus use the same document-level appearance.
window.csweetAppearance = {
    apply(theme) {
        const aliases = { night: "night-glass", aurora: "night-glass", day: "csweet-daylight", gold: "csweet-daylight" };
        const id = aliases[theme] || theme;
        const applied = window.csweetThemeIds.has(id) ? id : "night-glass";
        document.documentElement.dataset.appearance = applied;
        // Browser chrome follows the chosen theme, not the operating-system scheme.
        const paper = window.csweetThemePaper && window.csweetThemePaper[applied];
        if (paper) document.querySelectorAll('meta[name="theme-color"]').forEach(meta => meta.setAttribute("content", paper));
    }
};
try {
    window.csweetAppearance.apply(localStorage.getItem("csweet.appearance"));
} catch {
    window.csweetAppearance.apply("night-glass");
}
