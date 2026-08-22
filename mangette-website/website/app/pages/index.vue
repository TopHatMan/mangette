<template>
    <div class="arr-page">
        <div class="arr-page__head">
            <div>
                <h1 class="arr-page__title">Library</h1>
                <p class="arr-page__hint">{{ libraryHint }}</p>
            </div>
            <div class="flex flex-wrap gap-2 items-center">
                <UInput v-model="filter" icon="i-lucide-filter" placeholder="Filter" class="w-52" />
                <UButtonGroup>
                    <UButton
                        icon="i-lucide-layout-grid"
                        :variant="view === 'posters' ? 'solid' : 'outline'"
                        @click="view = 'posters'" />
                    <UButton icon="i-lucide-list" :variant="view === 'table' ? 'solid' : 'outline'" @click="view = 'table'" />
                </UButtonGroup>
                <UButton icon="i-lucide-folder-input" variant="outline" to="/import">Library Import</UButton>
                <UButton icon="i-lucide-plus" to="/search">Add New</UButton>
            </div>
        </div>

        <LoadingPage :loading="listPending">
            <div v-if="!series.length" class="flex flex-col items-center gap-3 py-20 text-center">
                <p class="text-lg font-medium">No series in this library yet</p>
                <p class="text-muted max-w-lg">
                    Search for a title and add only the series you want. Searching no longer dumps every match onto this page.
                    Existing folders can still be imported.
                </p>
                <div class="flex gap-2">
                    <UButton icon="i-lucide-plus" to="/search">Add New</UButton>
                    <UButton icon="i-lucide-folder-input" variant="outline" to="/import">Import Existing Library</UButton>
                </div>
            </div>

            <div v-else-if="view === 'posters'" class="arr-poster-grid">
                <SeriesPoster
                    v-for="m in filtered"
                    :key="m.key"
                    :title="m.name"
                    :src="libraryCover(m.key)"
                    :year="m.year"
                    :chapter-count="m.chapterCount"
                    :downloaded-count="m.downloadedCount"
                    :badge="m.monitored ? undefined : 'Off'"
                    :subtitle="chapterLabel(m)"
                    @click="navigateTo(`/manga/${m.key}`)" />
            </div>

            <div v-else class="overflow-x-auto">
                <table class="arr-table">
                    <thead>
                        <tr>
                            <th></th>
                            <th>Title</th>
                            <th>Status</th>
                            <th>Chapters</th>
                            <th>Sources</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="m in filtered" :key="m.key" class="cursor-pointer" @click="navigateTo(`/manga/${m.key}`)">
                            <td>
                                <img :src="libraryCover(m.key)" alt="" class="w-10 h-14 object-cover rounded" loading="lazy" decoding="async" />
                            </td>
                            <td>
                                <p class="font-medium">{{ m.name }}</p>
                                <p v-if="m.year" class="text-muted text-xs">{{ m.year }}</p>
                            </td>
                            <td>
                                <UBadge :color="m.monitored ? 'primary' : 'neutral'" variant="subtle" size="sm">
                                    {{ m.monitored ? 'Monitored' : 'Unmonitored' }}
                                </UBadge>
                                <span class="text-muted text-xs ml-2">{{ m.releaseStatus }}</span>
                            </td>
                            <td>
                                <div class="flex items-center gap-2 min-w-36">
                                    <div class="arr-progress">
                                        <div class="arr-progress__fill" :style="{ width: `${progress(m)}%` }" />
                                    </div>
                                    <span class="text-sm tabular-nums">{{ m.downloadedCount }}/{{ m.chapterCount }}</span>
                                </div>
                            </td>
                            <td>
                                <span class="text-sm text-muted">{{ (m.mangaConnectorIds ?? []).map((id) => id.mangaConnectorName).join(', ') }}</span>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </LoadingPage>
    </div>
</template>

<script setup lang="ts">
type LibrarySeries = {
    key: string;
    name: string;
    description: string;
    releaseStatus: string;
    year?: number | null;
    monitored: boolean;
    chapterCount: number;
    downloadedCount: number;
    mangaConnectorIds?: { mangaConnectorName: string }[];
};

const view = useState<'posters' | 'table'>('library-view', () => 'posters');
const filter = ref('');
const { data: manga, status } = await useApi('/v2/Manga', { key: FetchKeys.Manga.All, lazy: true, server: false });

const listPending = computed(() => manga.value == null && (status.value === 'pending' || status.value === 'idle'));
const series = computed(() => (manga.value ?? []) as unknown as LibrarySeries[]);
const filtered = computed(() => {
    const q = filter.value.trim().toLowerCase();
    if (!q) return series.value;
    return series.value.filter((m) => m.name.toLowerCase().includes(q));
});
const libraryHint = computed(() => (listPending.value ? 'Loading…' : `${filtered.value.length} series`));

const libraryCover = (key: string) => `/v2/Manga/${key}/Cover/Medium`;
const progress = (m: LibrarySeries) => (m.chapterCount > 0 ? Math.min(100, Math.round((m.downloadedCount / m.chapterCount) * 100)) : 0);
const chapterLabel = (m: LibrarySeries) =>
    m.chapterCount ? `${m.downloadedCount}/${m.chapterCount}` : m.monitored ? 'Looking for chapters' : '';

useHead({ title: 'Library' });
</script>
