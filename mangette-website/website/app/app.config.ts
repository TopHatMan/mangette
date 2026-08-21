export default defineAppConfig({
    ui: {
        colors: {
            primary: 'sky',
            secondary: 'slate',
            success: 'emerald',
            info: 'sky',
            warning: 'amber',
            error: 'red',
            neutral: 'zinc',
        },
        header: {
            slots: {
                root: 'border-b border-default bg-[#1d1f27]',
            },
        },
    },
});
