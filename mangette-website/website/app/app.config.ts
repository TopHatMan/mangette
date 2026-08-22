export default defineAppConfig({
    ui: {
        colors: {
            primary: 'amber',
            secondary: 'stone',
            success: 'emerald',
            info: 'amber',
            warning: 'orange',
            error: 'red',
            neutral: 'stone',
        },
        header: {
            slots: {
                root: 'border-b border-default bg-[#1a1410]',
            },
        },
    },
});
