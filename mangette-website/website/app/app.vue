<template>
    <UApp>
        <div class="arr-shell">
            <aside class="arr-sidebar">
                <NuxtLink to="/" class="arr-sidebar__brand">
                    <img src="/favicon.svg" alt="" />
                    <span>Mangette</span>
                </NuxtLink>
                <nav class="arr-sidebar__nav">
                    <NuxtLink v-for="item in items" :key="item.to" :to="item.to" class="arr-sidebar__link">
                        <UIcon :name="item.icon" class="size-5" />
                        <span>{{ item.label }}</span>
                    </NuxtLink>
                </nav>
                <p class="arr-sidebar__build">Series library · poster wall</p>
            </aside>
            <div class="arr-content">
                <header class="arr-topbar">
                    <p class="arr-topbar__title">{{ title }}</p>
                    <div class="flex items-center gap-2">
                        <UButton icon="i-lucide-plus" to="/search" color="primary" size="sm">Add New</UButton>
                        <UColorModeButton color="neutral" />
                    </div>
                </header>
                <main class="arr-content__main">
                    <NuxtPage />
                </main>
            </div>
        </div>
    </UApp>
</template>

<script setup lang="ts">
const route = useRoute();
const items = [
    { label: 'Library', to: '/', icon: 'i-lucide-layout-grid' },
    { label: 'Add New', to: '/search', icon: 'i-lucide-plus' },
    { label: 'Import', to: '/import', icon: 'i-lucide-folder-input' },
    { label: 'Activity', to: '/actions', icon: 'i-lucide-activity' },
    { label: 'Settings', to: '/settings', icon: 'i-lucide-settings' },
];
const title = computed(() => {
    const map: Record<string, string> = {
        '/': 'Library',
        '/search': 'Add New',
        '/import': 'Library Import',
        '/actions': 'Activity',
        '/settings': 'Settings',
    };
    if (route.path.startsWith('/manga/')) return 'Series';
    return map[route.path] ?? 'Mangette';
});
</script>
