window.cressStudio = window.cressStudio || {
    getRecentWorkspaces: function () {
        try {
            const raw = window.localStorage.getItem("cress.recentWorkspaces");
            if (!raw) {
                return [];
            }

            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    },

    setRecentWorkspaces: function (paths) {
        try {
            const normalized = Array.isArray(paths)
                ? paths.filter(path => typeof path === "string" && path.trim().length > 0).slice(0, 8)
                : [];
            window.localStorage.setItem("cress.recentWorkspaces", JSON.stringify(normalized));
        } catch {
            // Ignore localStorage persistence failures and keep the in-memory experience working.
        }
    },

    scrollSectionIntoView: function (id) {
        if (!id) {
            return;
        }

        const element = document.getElementById(id);
        if (!element) {
            return;
        }

        element.scrollIntoView({ behavior: "smooth", block: "start", inline: "nearest" });
    }
};
