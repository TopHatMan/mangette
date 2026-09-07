<template>
    <div class="w-full">
        <div class="flex flex-wrap items-center gap-2 mb-3">
            <p class="text-muted text-sm">{{ data?.totalCount ?? 0 }} chapters</p>
            <UButtonGroup>
                <UButton size="xs" :variant="dlFilter === 'all' ? 'solid' : 'outline'" @click="setDl('all')">All</UButton>
                <UButton size="xs" :variant="dlFilter === 'missing' ? 'solid' : 'outline'" @click="setDl('missing')">Missing</UButton>
                <UButton size="xs" :variant="dlFilter === 'have' ? 'solid' : 'outline'" @click="setDl('have')">Downloaded</UButton>
            </UButtonGroup>
            <UInput v-model="filter.name" size="sm" class="w-40" placeholder="Title" />
            <UPagination
                size="sm"
                :default-page="pagination.pageIndex + 1"
                :items-per-page="pagination.pageSize"
                :total="data?.totalCount ?? 0"
                class="ml-auto"
                @update:page="(p) => (pagination.pageIndex = p - 1)" />
        </div>

        <div class="overflow-x-auto">
            <table class="arr-table">
                <thead>
                    <tr>
                        <th class="w-8"></th>
                        <th>Volume</th>
                        <th>Chapter</th>
                        <th>Title</th>
                        <th>File</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="ch in data?.data ?? []" :id="ch.key" :key="ch.key">
                        <td>
                            <UTooltip :text="ch.downloaded ? 'On disk' : 'Missing'">
                                <UIcon
                                    :name="ch.downloaded ? 'i-lucide-circle-check' : 'i-lucide-circle-dashed'"
                                    :class="ch.downloaded ? 'text-success' : 'text-warning'"
                                    class="size-4" />
                            </UTooltip>
                        </td>
                        <td class="tabular-nums text-muted">{{ ch.volume ?? '—' }}</td>
                        <td class="tabular-nums font-medium">{{ ch.chapterNumber }}</td>
                        <td class="min-w-40">{{ chapterLabel(ch) }}</td>
                        <td class="text-muted text-xs truncate max-w-56" :title="ch.fileName ?? ''">{{ ch.fileName || '—' }}</td>
                        <td class="whitespace-nowrap">
                            <UTooltip text="Interactive search">
                                <UButton
                                    size="xs"
                                    variant="ghost"
                                    icon="i-lucide-list"
                                    :disabled="ch.downloaded"
                                    @click="openInteractive(ch)" />
                            </UTooltip>
                            <UTooltip text="Search this chapter">
                                <UButton
                                    size="xs"
                                    variant="ghost"
                                    icon="i-lucide-search"
                                    :disabled="ch.downloaded"
                                    :loading="grabbing === ch.key"
                                    @click="automaticSearch(ch)" />
                            </UTooltip>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <UModal v-model:open="searchOpen" title="Interactive Search">
            <template #body>
                <p class="text-muted text-sm mb-3">
                    {{ searchChapter ? `Ch. ${searchChapter.chapterNumber}` : '' }}
                    {{ searchChapter?.title ? ` — ${searchChapter.title}` : '' }}
                </p>
                <p v-if="searchBusy" class="text-muted text-sm">{{ isComic ? 'Searching Prowlarr…' : 'Looking up sites…' }}</p>
                <template v-else-if="isComic">
                    <p v-if="!comicReleases.length" class="text-muted text-sm">
                        No Prowlarr releases found. Check the search query, or your indexers.
                    </p>
                    <div v-else class="overflow-x-auto max-h-96 overflow-y-auto">
                        <table class="arr-table">
                            <thead>
                                <tr>
                                    <th class="cursor-pointer select-none" @click="toggleSort('title')">Release{{ sortArrow('title') }}</th>
                                    <th class="cursor-pointer select-none" @click="toggleSort('indexerName')">
                                        Indexer{{ sortArrow('indexerName') }}
                                    </th>
                                    <th class="cursor-pointer select-none" @click="toggleSort('protocol')">
                                        Protocol{{ sortArrow('protocol') }}
                                    </th>
                                    <th class="cursor-pointer select-none" @click="toggleSort('size')">Size{{ sortArrow('size') }}</th>
                                    <th class="cursor-pointer select-none" @click="toggleSort('seeders')">
                                        Seeders{{ sortArrow('seeders') }}
                                    </th>
                                    <th class="cursor-pointer select-none" @click="toggleSort('publishDate')">
                                        Age{{ sortArrow('publishDate') }}
                                    </th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="rel in sortedComicReleases" :key="rel.downloadUrl">
                                    <td class="max-w-64 truncate font-medium text-sm" :title="rel.title">{{ rel.title }}</td>
                                    <td class="text-muted text-xs">{{ rel.indexerName }}</td>
                                    <td class="text-xs">
                                        <UBadge size="sm" variant="subtle" :color="rel.protocol === 'Usenet' ? 'secondary' : 'primary'">
                                            {{ rel.protocol }}
                                        </UBadge>
                                    </td>
                                    <td class="tabular-nums text-xs whitespace-nowrap">{{ formatBytes(rel.size) }}</td>
                                    <td class="tabular-nums text-xs">{{ rel.seeders ?? '—' }}</td>
                                    <td class="text-xs whitespace-nowrap">{{ formatAge(rel.publishDate) }}</td>
                                    <td>
                                        <UButton size="xs" :loading="grabbingComic === rel.downloadUrl" @click="grabComic(rel)">Download</UButton>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </template>
                <p v-else-if="!releases.length" class="text-muted text-sm">
                    No sites attached for this chapter. Add a site on the series first.
                </p>
                <div v-else class="flex flex-col gap-2">
                    <div v-for="rel in releases" :key="rel.key" class="flex items-center gap-2 bg-elevated rounded-lg p-2">
                        <div class="grow min-w-0">
                            <p class="font-medium text-sm">{{ rel.connectorName }}</p>
                            <p class="text-muted text-xs truncate">{{ rel.websiteUrl || rel.idOnSite }}</p>
                        </div>
                        <UBadge v-if="rel.preferred" size="sm" color="success" variant="subtle">Preferred</UBadge>
                        <UButton size="xs" :loading="grabbing === rel.key" @click="grab(rel.connectorName)">Download</UButton>
                    </div>
                </div>
            </template>
        </UModal>
    </div>
</template>

<script setup lang="ts">
type Chapter = {
    key: string;
    volume?: number | null;
    chapterNumber: string;
    title?: string | null;
    downloaded: boolean;
    fileName?: string | null;
    mangaConnectorIds?: { key: string; mangaConnectorName: string; useForDownload: boolean }[];
};
type Release = {
    key: string;
    connectorName: string;
    idOnSite: string;
    websiteUrl?: string | null;
    preferred: boolean;
    title?: string | null;
};
type ComicRelease = {
    title: string;
    downloadUrl: string;
    infoUrl?: string | null;
    protocol: 'Torrent' | 'Usenet';
    indexerName: string;
    size: number;
    publishDate: string;
    seeders?: number | null;
};

const filter = ref<{ name?: string; downloaded?: boolean }>({});
const dlFilter = ref<'all' | 'missing' | 'have'>('all');
const pagination = ref({ pageIndex: 0, pageSize: 50 });

const props = defineProps<{ mangaId: string; kind?: 'Manga' | 'Comic' }>();
const isComic = computed(() => props.kind === 'Comic');
const { $api } = useNuxtApp();
const toast = useToast();

const formatBytes = (bytes: number) => {
    if (!bytes) return '0 MB';
    const mb = bytes / 1024 / 1024;
    return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
};

const formatAge = (iso: string) => {
    const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
    if (days <= 0) return 'today';
    if (days < 31) return `${days}d`;
    if (days < 365) return `${Math.floor(days / 30)}mo`;
    return `${Math.floor(days / 365)}y`;
};

type ComicSortKey = 'title' | 'indexerName' | 'protocol' | 'size' | 'seeders' | 'publishDate';
const sortKey = ref<ComicSortKey | null>(null);
const sortDir = ref<'asc' | 'desc'>('desc');
const toggleSort = (key: ComicSortKey) => {
    if (sortKey.value === key) {
        sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc';
    } else {
        sortKey.value = key;
        sortDir.value = key === 'title' || key === 'indexerName' || key === 'protocol' ? 'asc' : 'desc';
    }
};
const sortArrow = (key: ComicSortKey) => (sortKey.value === key ? (sortDir.value === 'asc' ? ' ▲' : ' ▼') : '');

const { data, refresh } = useAsyncData(
    FetchKeys.Chapters.Manga(props.mangaId),
    () =>
        $api('/v2/Chapters/Manga/{MangaId}', {
            method: 'POST',
            query: { page: pagination.value.pageIndex + 1, pageSize: pagination.value.pageSize },
            path: { MangaId: props.mangaId },
            body: filter.value,
        }),
    { watch: [pagination, filter], lazy: true, server: false },
);

const setDl = (mode: 'all' | 'missing' | 'have') => {
    dlFilter.value = mode;
    filter.value = {
        ...filter.value,
        downloaded: mode === 'all' ? undefined : mode === 'have',
    };
    pagination.value = { ...pagination.value, pageIndex: 0 };
};

const chapterLabel = (ch: Chapter) => {
    if (ch.title && ch.title !== ch.chapterNumber) return ch.title;
    const vol = ch.volume != null ? `Vol. ${ch.volume} ` : '';
    return `${vol}Ch. ${ch.chapterNumber}`.trim();
};

const searchOpen = ref(false);
const searchBusy = ref(false);
const searchChapter = ref<Chapter | null>(null);
const releases = ref<Release[]>([]);
const comicReleases = ref<ComicRelease[]>([]);
const grabbing = ref('');
const grabbingComic = ref('');

const sortedComicReleases = computed(() => {
    if (!sortKey.value) return comicReleases.value;
    const key = sortKey.value;
    const dir = sortDir.value === 'asc' ? 1 : -1;
    return [...comicReleases.value].sort((a, b) => {
        let av: string | number = a[key] ?? '';
        let bv: string | number = b[key] ?? '';
        if (key === 'publishDate') {
            av = new Date(a.publishDate).getTime();
            bv = new Date(b.publishDate).getTime();
        } else if (key === 'seeders') {
            av = a.seeders ?? -1;
            bv = b.seeders ?? -1;
        }
        if (typeof av === 'string' && typeof bv === 'string') return av.localeCompare(bv) * dir;
        return ((av as number) - (bv as number)) * dir;
    });
});

const openInteractive = async (ch: Chapter) => {
    searchChapter.value = ch;
    searchOpen.value = true;
    searchBusy.value = true;
    releases.value = [];
    comicReleases.value = [];
    sortKey.value = null;
    try {
        if (isComic.value) {
            comicReleases.value = (await $fetch<ComicRelease[]>(`/v2/Comic/Chapters/${encodeURIComponent(ch.key)}/Releases`)) ?? [];
        } else {
            releases.value = (await $fetch<Release[]>(`/v2/Chapters/${encodeURIComponent(ch.key)}/Releases`)) ?? [];
        }
    } catch {
        releases.value = [];
        comicReleases.value = [];
    } finally {
        searchBusy.value = false;
    }
};

const grab = async (connectorName?: string) => {
    const id = searchChapter.value?.key;
    if (!id) return;
    grabbing.value = connectorName ?? id;
    try {
        await $fetch(`/v2/Chapters/${encodeURIComponent(id)}/Grab`, {
            method: 'POST',
            body: { connectorName: connectorName ?? null },
        });
        searchOpen.value = false;
        await refresh();
    } finally {
        grabbing.value = '';
    }
};

const grabComic = async (release: ComicRelease) => {
    const id = searchChapter.value?.key;
    if (!id) return;
    grabbingComic.value = release.downloadUrl;
    try {
        await $fetch(`/v2/Comic/Chapters/${encodeURIComponent(id)}/Grab`, {
            method: 'POST',
            body: { release },
        });
        searchOpen.value = false;
        await refresh();
    } finally {
        grabbingComic.value = '';
    }
};

// "Search this chapter" auto-picks the best matching release and grabs it immediately -- for
// comics that means a fresh Prowlarr search filtered to a release whose title parses to this
// issue number, same idea as manga auto-picking its preferred attached site.
const automaticSearch = async (ch: Chapter) => {
    grabbing.value = ch.key;
    try {
        if (isComic.value) {
            await $fetch(`/v2/Comic/Chapters/${encodeURIComponent(ch.key)}/Grab`, { method: 'POST', body: { release: null } });
        } else {
            await $fetch(`/v2/Chapters/${encodeURIComponent(ch.key)}/Grab`, { method: 'POST', body: {} });
        }
        await refresh();
    } catch (e: unknown) {
        if (isComic.value) {
            const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
            toast.add({ title: body || 'Could not auto-grab a release for this issue.', color: 'error' });
        }
    } finally {
        grabbing.value = '';
    }
};
</script>
