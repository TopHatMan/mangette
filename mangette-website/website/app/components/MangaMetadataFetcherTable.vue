<template>
    <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
            <h1 class="font-semibold">Metadata</h1>
            <p class="text-muted text-xs font-normal mt-1">
                AniList/MAL fill the series name and synopsis. Chapter titles come from sites or the MangaDex catalog (not used for downloads).
            </p>
        </template>
        <UTable
            v-if="metadataFetchers && metadata"
            :data="metadataFetchers"
            :columns="[
                { header: 'Name', id: 'name' },
                { header: '', id: 'link' },
            ]">
            <template #name-cell="{ row }">
                <UTooltip :text="metadata.find((me) => me.metadataFetcherName == row.original)?.identifier ?? undefined">
                    <p class="text-toned">{{ row.original }}</p></UTooltip
                >
            </template>
            <template #link-cell="{ row }">
                <div class="flex flex-row gap-2 justify-end">
                    <UButton
                        v-if="metadata.find((me) => me.metadataFetcherName === row.original)"
                        icon="i-lucide-unlink"
                        loading-auto
                        @click="unlinkMetadataFetcher(row.original)" />
                    <UTooltip v-if="metadata.find((me) => me.metadataFetcherName === row.original)" text="Update Metadata">
                        <UButton icon="i-lucide-refresh-ccw-dot" loading-auto @click="updateMetadata(row.original)" />
                    </UTooltip>
                    <UButton
                        v-if="metadata.find((me) => me.metadataFetcherName === row.original) === undefined"
                        :to="`/manga/${mangaId}/linkMetadata/${row.original}?return=${$route.fullPath}`"
                        loading-auto
                        icon="i-lucide-link" />
                </div>
            </template>
        </UTable>
        <div class="p-3 border-t border-default">
            <UButton size="sm" variant="outline" icon="i-lucide-book-text" :loading="fillingTitles" @click="fillTitles">
                Fill chapter titles
            </UButton>
            <p v-if="fillMessage" class="text-muted text-xs mt-2">{{ fillMessage }}</p>
        </div>
    </UCard>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string }>();
const mangaId = props.mangaId;

const { $api } = useNuxtApp();

const { data: metadataFetchers } = await useApi('/v2/MetadataFetcher', { key: FetchKeys.Metadata.Fetchers, lazy: true, server: false });
const { data: metadata } = await useApi('/v2/MetadataFetcher/Links/{MangaId}', {
    path: { MangaId: mangaId },
    key: FetchKeys.Metadata.Manga(mangaId),
    lazy: true,
    server: false,
});

const unlinkMetadataFetcher = async (metadataFetcherName: string) => {
    await $api('/v2/MetadataFetcher/{MetadataFetcherName}/Unlink/{MangaId}', {
        method: 'POST',
        path: { MangaId: mangaId, MetadataFetcherName: metadataFetcherName },
    });
    await refreshNuxtData(FetchKeys.Metadata.Manga(mangaId));
};

const updateMetadata = async (metadataFetcherName: string) => {
    await $api('/v2/MetadataFetcher/{MetadataFetcherName}/Update/{MangaId}', {
        method: 'POST',
        path: { MangaId: mangaId, MetadataFetcherName: metadataFetcherName },
    });
    await refreshNuxtData([FetchKeys.Manga.Id(mangaId), FetchKeys.Chapters.Manga(mangaId)]);
};

const fillingTitles = ref(false);
const fillMessage = ref('');
const fillTitles = async () => {
    fillingTitles.value = true;
    fillMessage.value = '';
    try {
        const n = await $fetch<number>(`/v2/Manga/${encodeURIComponent(mangaId)}/FillChapterTitles`, { method: 'POST' });
        fillMessage.value = n ? `Filled ${n} title/volume field${n === 1 ? '' : 's'}.` : 'No extra titles found (or MangaDex had no close match).';
        await refreshNuxtData(FetchKeys.Chapters.Manga(mangaId));
    } catch {
        fillMessage.value = 'Could not look up chapter titles.';
    } finally {
        fillingTitles.value = false;
    }
};
</script>
