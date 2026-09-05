<template>
    <div class="arr-page">
        <div class="arr-page__head">
            <div>
                <h1 class="arr-page__title">Discover</h1>
                <p class="arr-page__hint">{{ hint }}</p>
            </div>
            <div class="flex flex-wrap gap-2 items-center">
                <UButtonGroup>
                    <UButton :variant="kind === 'all' ? 'solid' : 'outline'" @click="kind = 'all'">All</UButton>
                    <UButton :variant="kind === 'Manga' ? 'solid' : 'outline'" @click="kind = 'Manga'">Manga</UButton>
                    <UButton :variant="kind === 'Manhwa' ? 'solid' : 'outline'" @click="kind = 'Manhwa'">Manhwa</UButton>
                    <UButton :variant="kind === 'Manhua' ? 'solid' : 'outline'" @click="kind = 'Manhua'">Manhua</UButton>
                </UButtonGroup>
                <UButton icon="i-lucide-refresh-ccw" variant="outline" :loading="refreshing" @click="reload(true)">Refresh</UButton>
            </div>
        </div>

        <p v-if="loadError" class="text-error text-sm mb-4">{{ loadError }}</p>

        <div v-else-if="pending && !page" class="text-muted py-16 text-center">Loading AniList catalogs…</div>

        <div v-else class="flex flex-col gap-8">
            <section v-for="shelf in shelves" :key="shelf.id">
                <div class="mb-3">
                    <h2 class="text-lg font-semibold">{{ shelf.title }}</h2>
                    <p class="text-muted text-sm">{{ shelf.hint }}</p>
                </div>
                <p v-if="!shelf.items.length" class="text-muted text-sm">Nothing in this list right now.</p>
                <div v-else class="discover-shelf">
                    <SeriesPoster
                        v-for="hit in shelf.items"
                        :key="`${shelf.id}:${hit.aniListId}:${hit.idOnSite}:${hit.name}`"
                        :title="hit.name"
                        :src="coverSrc(hit.coverUrl)"
                        :year="hit.year"
                        :badge="hit.alreadyInLibrary ? 'Added' : scoreBadge(hit)"
                        :subtitle="posterSub(hit)"
                        :show-monitor="false"
                        @click="openHit(hit)" />
                </div>
            </section>
        </div>

        <UModal v-model:open="detailOpen" :title="selected?.name ?? 'Series'">
            <template #body>
                <div v-if="selected" class="flex flex-col gap-4">
                    <div class="flex gap-3">
                        <img :src="coverSrc(selected.coverUrl)" alt="" class="w-24 h-36 object-cover rounded-md" />
                        <div class="min-w-0">
                            <p class="font-semibold text-lg">{{ selected.name }}</p>
                            <p class="text-muted text-sm">
                                {{ selected.kind }}
                                <span v-if="selected.year"> · {{ selected.year }}</span>
                                <span v-if="selected.averageScore"> · {{ selected.averageScore }}%</span>
                            </p>
                            <div class="flex flex-wrap gap-1 mt-2">
                                <UBadge v-for="g in selected.genres ?? []" :key="g" size="sm" variant="subtle" color="neutral">{{ g }}</UBadge>
                            </div>
                        </div>
                    </div>
                    <p class="text-muted text-sm line-clamp-6">{{ selected.description || 'No overview.' }}</p>

                    <div v-if="selected.alreadyInLibrary && selected.existingMangaId">
                        <UButton :to="`/manga/${selected.existingMangaId}`" block>Open in library</UButton>
                    </div>
                    <div v-else class="flex flex-col gap-3">
                        <UFormField label="Root folder">
                            <USelect v-model="libraryId" :items="libraryItems" class="w-full" />
                        </UFormField>
                        <UCheckbox v-model="monitor" label="Monitor and download missing chapters" />
                        <UFormField v-if="monitor" label="Check for new chapters">
                            <USelect v-model="newChapterCheck" :items="checkItems" class="w-full" />
                        </UFormField>
                        <p v-if="lookupBusy" class="text-muted text-sm">Looking it up on your download sites…</p>
                        <p v-else-if="lookupError" class="text-error text-sm">{{ lookupError }}</p>
                        <div v-else-if="lookupHits.length" class="flex flex-col gap-2 max-h-56 overflow-y-auto">
                            <button
                                v-for="hit in lookupHits"
                                :key="`${hit.connectorName}:${hit.idOnSite}`"
                                class="text-left bg-elevated rounded-lg p-2 hover:bg-accented"
                                :class="pickedKey === hitKey(hit) ? 'ring-1 ring-primary' : ''"
                                @click="picked = hit">
                                <p class="font-medium">{{ hit.name }}</p>
                                <p class="text-muted text-xs">{{ hit.connectorName }}</p>
                            </button>
                        </div>
                        <p v-if="addError" class="text-error text-sm">{{ addError }}</p>
                    </div>
                </div>
            </template>
            <template #footer>
                <div class="flex justify-end gap-2">
                    <UButton variant="ghost" @click="detailOpen = false">Close</UButton>
                    <UButton
                        v-if="selected && !selected.alreadyInLibrary"
                        :loading="adding || lookupBusy"
                        :disabled="!canAdd"
                        @click="addSelected">
                        Add Series
                    </UButton>
                </div>
            </template>
        </UModal>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type FileLibrary = components['schemas']['FileLibrary'];

type DiscoverHit = {
    name: string;
    description: string;
    year?: number | null;
    releaseStatus: string;
    coverUrl: string;
    aniListId: number;
    siteUrl: string;
    averageScore?: number | null;
    popularity: number;
    kind: string;
    genres: string[];
    chapters?: number | null;
    alreadyInLibrary: boolean;
    existingMangaId?: string | null;
    connectorName?: string | null;
    idOnSite?: string | null;
};
type DiscoverPage = {
    trending: DiscoverHit[];
    popular: DiscoverHit[];
    newReleases: DiscoverHit[];
    recentlyUpdated: DiscoverHit[];
    popularOnSites: DiscoverHit[];
    generatedAtUtc?: string;
    source?: string;
};
type SearchHit = {
    name: string;
    description: string;
    year?: number | null;
    releaseStatus: string;
    coverUrl: string;
    connectorName: string;
    idOnSite: string;
    score: number;
    alreadyInLibrary: boolean;
    existingMangaId?: string | null;
};
type AddResult = { key: string };

const { data: libraries } = await useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });
const libraryItems = computed(() =>
    (libraries.value ?? []).map((l: FileLibrary) => ({ label: `${l.libraryName} (${l.basePath})`, value: l.key })),
);

const page = ref<DiscoverPage | null>(null);
const pending = ref(true);
const refreshing = ref(false);
const loadError = ref('');
const kind = ref<'all' | 'Manga' | 'Manhwa' | 'Manhua'>('all');

const filterKind = (hits: DiscoverHit[] | undefined) => {
    const rows = hits ?? [];
    if (kind.value === 'all') return rows;
    return rows.filter((h) => h.kind === kind.value);
};

const shelves = computed(() => {
    const rows = [
        { id: 'trending', title: 'Trending now', hint: 'What people are reading this week on AniList.', items: filterKind(page.value?.trending) },
        { id: 'popular', title: 'Popular', hint: 'All-time most followed titles.', items: filterKind(page.value?.popular) },
        { id: 'new', title: 'New series', hint: 'Recently started or not yet released.', items: filterKind(page.value?.newReleases) },
        { id: 'updated', title: 'Recently updated', hint: 'Ongoing series that just got a new chapter listing.', items: filterKind(page.value?.recentlyUpdated) },
        { id: 'sites', title: 'Popular on your sites', hint: 'WeebCentral popularity, when that site is reachable.', items: filterKind(page.value?.popularOnSites) },
    ];
    return rows.filter((s) => s.id !== 'sites' || s.items.length > 0);
});

const hint = computed(() => {
    if (pending.value && !page.value) return 'Loading…';
    const n = shelves.value.reduce((sum, s) => sum + s.items.length, 0);
    return `${n} titles from AniList. Add any of them to your library.`;
});

const coverSrc = (url: string) => (url ? `/v2/Search/Cover?url=${encodeURIComponent(url)}` : '');
const scoreBadge = (hit: DiscoverHit) => (hit.averageScore ? `${hit.averageScore}%` : undefined);
const posterSub = (hit: DiscoverHit) => [hit.kind, hit.year].filter(Boolean).join(' · ');

const reload = async (refresh = false) => {
    if (refresh) refreshing.value = true;
    else pending.value = true;
    loadError.value = '';
    try {
        const qs = refresh ? '?refresh=true' : '';
        page.value = await $fetch<DiscoverPage>(`/v2/Discover${qs}`);
    } catch {
        loadError.value = 'Could not load Discover. AniList may be unreachable.';
    } finally {
        pending.value = false;
        refreshing.value = false;
    }
};

onMounted(() => {
    void reload();
});

const detailOpen = ref(false);
const selected = ref<DiscoverHit | null>(null);
const libraryId = ref<string | undefined>();
const monitor = ref(true);
const newChapterCheck = ref('Daily');
const checkItems = [
    { label: 'Daily', value: 'Daily' },
    { label: 'Weekly', value: 'Weekly' },
];
const lookupBusy = ref(false);
const lookupError = ref('');
const lookupHits = ref<SearchHit[]>([]);
const picked = ref<SearchHit | null>(null);
const adding = ref(false);
const addError = ref('');
const pickedKey = computed(() => (picked.value ? hitKey(picked.value) : ''));
const hitKey = (h: SearchHit) => `${h.connectorName}:${h.idOnSite}`;
const canAdd = computed(() => !!picked.value && !!libraryId.value);

const openHit = async (hit: DiscoverHit) => {
    selected.value = hit;
    detailOpen.value = true;
    addError.value = '';
    lookupError.value = '';
    lookupHits.value = [];
    picked.value = null;
    monitor.value = true;
    newChapterCheck.value = 'Daily';
    libraryId.value = libraries.value?.[0]?.key;
    if (hit.alreadyInLibrary) return;
    if (hit.connectorName && hit.idOnSite) {
        picked.value = {
            name: hit.name,
            description: hit.description,
            year: hit.year,
            releaseStatus: hit.releaseStatus,
            coverUrl: hit.coverUrl,
            connectorName: hit.connectorName,
            idOnSite: hit.idOnSite,
            score: 100,
            alreadyInLibrary: false,
        };
        return;
    }
    lookupBusy.value = true;
    try {
        const hits = await $fetch<SearchHit[]>(`/v2/Search/Lookup?query=${encodeURIComponent(hit.name)}`);
        lookupHits.value = hits ?? [];
        const best = lookupHits.value.find((h) => !h.alreadyInLibrary);
        picked.value = best ?? lookupHits.value[0] ?? null;
        if (!lookupHits.value.length)
            lookupError.value = 'None of your download sites have this title yet. Try Add New with a different name.';
    } catch {
        lookupError.value = 'Lookup failed. Try Add New.';
    } finally {
        lookupBusy.value = false;
    }
};

const addSelected = async () => {
    if (!picked.value) return;
    adding.value = true;
    addError.value = '';
    try {
        const added = await $fetch<AddResult>('/v2/Search/Add', {
            method: 'POST',
            body: {
                connectorName: picked.value.connectorName,
                idOnSite: picked.value.idOnSite,
                libraryId: libraryId.value,
                monitor: monitor.value,
                newChapterCheck: monitor.value ? newChapterCheck.value : undefined,
            },
        });
        detailOpen.value = false;
        await reload();
        await refreshNuxtData(FetchKeys.Manga.All);
        if (added?.key) await navigateTo(`/manga/${added.key}`);
    } catch (e: unknown) {
        addError.value = e instanceof Error ? e.message : 'Could not add that series.';
    } finally {
        adding.value = false;
    }
};

useHead({ title: 'Discover' });
</script>
