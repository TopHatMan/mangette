<template>
    <UApp>
        <UHeader :toggle="true">
            <template #left>
                <NuxtLink to="/">
                    <div class="h-full flex gap-2 items-center">
                        <img src="/favicon.svg" class="h-7 w-7" alt="Mangette" />
                        <p class="font-bold cursor-pointer text-3xl text-highlighted">Mangette</p>
                    </div>
                </NuxtLink>
            </template>
            <template #body>
                <UNavigationMenu :items="items" orientation="vertical" variant="link" color="neutral" />
            </template>
            <template #default>
                <UNavigationMenu :items="items" orientation="horizontal" variant="link" color="neutral" />
            </template>
            <template #right>
                <UButton
                    icon="i-lucide-brick-wall-shield"
                    :to="`/actions?return=${$route.fullPath}`"
                    :disabled="$route.fullPath.startsWith('/actions')"
                    variant="soft"
                    color="neutral" />
                <UButton icon="i-lucide-plus" to="/search" color="primary">
                    <template #default>
                        <span class="max-sm:hidden">Manga</span>
                    </template>
                </UButton>
                <UColorModeButton color="neutral" />
                <UButton icon="i-lucide-settings" variant="ghost" to="/settings" color="neutral" />
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
    { label: 'Swagger', to: `${useRuntimeConfig().public.openFetch.api.baseURL}swagger`, icon: 'i-lucide-book-open', target: '_blank' },
]);
</script>
