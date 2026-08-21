<template>
    <div class="px-4 py-3">
        <div class="flex flex-wrap items-center justify-between gap-3 mb-4">
            <div>
                <h1 class="text-xl font-semibold">Library</h1>
                <p class="text-muted text-sm">{{ manga?.length ?? 0 }} series</p>
            </div>
            <div class="flex gap-2">
                <UButton icon="i-lucide-folder-input" variant="outline" to="/import">Library Import</UButton>
                <UButton icon="i-lucide-plus" to="/search">Add New</UButton>
            </div>
        </div>
        <LoadingPage :loading="status === 'pending'">
            <div v-if="!manga?.length" class="flex flex-col items-center gap-3 py-20 text-center">
                <p class="text-lg font-medium">No series in this library yet</p>
                <p class="text-muted max-w-lg">
                    If you already have a folder of manga from Tranga, import it. Mangette will match folder names to sites and skip
                    chapters you already have.
                </p>
                <div class="flex gap-2">
                    <UButton icon="i-lucide-folder-input" to="/import">Import Existing Library</UButton>
                    <UButton icon="i-lucide-plus" variant="outline" to="/search">Add New</UButton>
                </div>
            </div>
            <MangaCardList v-else :manga="manga" class="mt-2" @click="(m) => navigateTo(`/manga/${m.key}`)" />
        </LoadingPage>
    </div>
</template>

<script setup lang="ts">
const { data: manga, refresh, status } = await useApi('/v2/Manga', { key: FetchKeys.Manga.All, lazy: true, server: false });
onMounted(() => refresh());
useHead({ title: 'Library' });
</script>
