<template>
    <UApp>
        <UHeader :toggle="true">
            <template #left>
                <NuxtLink to="/" class="flex gap-2 items-center">
                    <img src="/favicon.svg" class="h-7 w-7" alt="Mangette" />
                    <p class="font-semibold text-lg tracking-tight">Mangette</p>
                </NuxtLink>
            </template>
            <template #body>
                <UNavigationMenu :items="items" orientation="vertical" variant="link" color="neutral" />
            </template>
            <template #default>
                <UNavigationMenu :items="items" orientation="horizontal" variant="link" color="neutral" />
            </template>
            <template #right>
                <UButton icon="i-lucide-plus" to="/search" color="primary">Add New</UButton>
                <UColorModeButton color="neutral" />
            </template>
        </UHeader>
        <UMain>
            <UPage>
                <NuxtPage />
            </UPage>
        </UMain>
    </UApp>
</template>

<script setup lang="ts">
import type { NavigationMenuItem } from '#ui/components/NavigationMenu.vue';

const items = computed<NavigationMenuItem[]>(() => [
    { label: 'Library', to: '/', icon: 'i-lucide-layout-grid' },
    { label: 'Add New', to: '/search', icon: 'i-lucide-plus' },
    { label: 'Library Import', to: '/import', icon: 'i-lucide-folder-input' },
    { label: 'Activity', to: '/actions', icon: 'i-lucide-activity' },
    { label: 'Settings', to: '/settings', icon: 'i-lucide-settings' },
]);
</script>
