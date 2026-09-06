<template>
    <MangaDetailPage :manga="manga">
        <div class="grid gap-3 max-xl:grid-flow-row-dense min-2xl:grid-cols-[70%_auto] min-xl:grid-cols-[60%_auto] relative min-xl:h-full">
            <ChaptersList :manga-id="mangaId" :kind="manga?.kind" class="min-xl:h-full min-xl:overflow-y-scroll" />
            <div class="flex flex-col gap-2">
                <UCard :class="[flashDownloading ? 'animate-[flash_0.75s_ease_0.5s]' : '']">
                    <template #header>
                        <h1 class="font-semibold">Download</h1>
                    </template>
                    <p class="text-muted text-xs mb-3">
                        Monitor to download missing chapters and scan for new ones. Ongoing series grow their chapter total on Daily or Weekly.
                    </p>
                    <div class="flex items-center justify-between gap-2 mb-3">
                        <span class="text-sm font-medium">Monitor</span>
                        <USwitch :model-value="!!manga?.monitored" :disabled="!manga || togglingMonitor" @update:model-value="setMonitored" />
                    </div>
                    <UFormField v-if="manga?.monitored" label="Check for new chapters" class="mb-3">
                        <USelect
                            :model-value="manga.newChapterCheck ?? 'Daily'"
                            :items="checkItems"
                            class="w-full"
                            :disabled="togglingMonitor"
                            @update:model-value="setCheckInterval" />
                    </UFormField>
                    <p v-if="manga?.monitored && lastCheckLabel" class="text-muted text-xs mb-3">Last scan {{ lastCheckLabel }}</p>
                    <template v-if="manga?.kind === 'Comic'">
                        <p class="text-muted text-xs mb-3">
                            Comics are searched on Prowlarr automatically every few minutes, or per-issue from the list
                            on the left ("Interactive search"). There's no site to attach here.
                        </p>
                    </template>
                    <template v-else>
                        <UButton
                            class="mb-2 w-full"
                            icon="i-lucide-list-plus"
                            size="sm"
                            :loading="refreshingChapters"
                            :disabled="!manga?.fileLibraryId"
                            @click="refreshChapters">
                            Refresh chapter list
                        </UButton>
                        <UButton
                            class="mb-3 w-full"
                            icon="i-lucide-search"
                            size="sm"
                            variant="outline"
                            :loading="searchingMissing"
                            :disabled="!manga?.fileLibraryId || !manga?.monitored"
                            @click="searchMissing">
                            Search missing
                        </UButton>
                    </template>
                    <LibrarySelect
                        :manga-id="mangaId"
                        :library-id="manga?.fileLibraryId"
                        class="w-full"
                        @library-changed="refreshNuxtData(FetchKeys.Manga.Id(mangaId))" />
                    <div v-if="manga?.kind === 'Comic'" class="mt-3">
                        <h2 class="text-sm font-medium mb-2">Active downloads</h2>
                        <ComicQueueTable :manga-id="mangaId" :poll-interval-ms="10000" />
                    </div>
                    <div v-if="manga && manga.kind !== 'Comic'" class="flex flex-col gap-2 mt-3">
                        <div
                            v-for="site in availableSites"
                            :key="site.name"
                            class="flex items-center gap-2 bg-elevated rounded-lg p-2">
                            <span class="grow text-sm">{{ site.name }}</span>
                            <UBadge v-if="linkOf(site.name)?.useForDownload" color="success" variant="subtle" size="sm">On</UBadge>
                            <UBadge v-else-if="linkOf(site.name)" color="neutral" variant="subtle" size="sm">Off</UBadge>
                            <UButton
                                v-if="linkOf(site.name)"
                                size="xs"
                                :variant="linkOf(site.name)?.useForDownload ? 'outline' : 'solid'"
                                :disabled="!manga?.fileLibraryId"
                                @click="setRequestedFrom(site.name, !linkOf(site.name)?.useForDownload)">
                                {{ linkOf(site.name)?.useForDownload ? 'Stop using' : 'Use' }}
                            </UButton>
                            <UButton v-else size="xs" variant="outline" :disabled="!manga?.fileLibraryId" @click="openAddSite(site.name)">
                                Add this site
                            </UButton>
                        </div>
                    </div>
                    <UModal v-model:open="addSiteOpen" :title="`Add ${addSiteName}`">
                        <template #body>
                            <p class="text-muted text-sm mb-3">
                                Search {{ addSiteName }} for this series, then pick the matching title.
                            </p>
                            <form class="flex gap-2 mb-3" @submit.prevent="searchAddSite">
                                <UInput
                                    v-model="addSiteQuery"
                                    class="grow"
                                    icon="i-lucide-search"
                                    placeholder="Series name"
                                    autofocus />
                                <UButton type="submit" :loading="addSiteBusy" :disabled="!addSiteQuery.trim()">Search</UButton>
                            </form>
                            <p v-if="addSiteBusy" class="text-muted text-sm">Searching…</p>
                            <p v-else-if="addSiteSearched && !addSiteHits.length" class="text-muted text-sm">
                                No results for “{{ addSiteLastQuery }}”.
                            </p>
                            <div v-else class="flex flex-col gap-2 max-h-80 overflow-y-auto">
                                <button
                                    v-for="hit in addSiteHits"
                                    :key="`${hit.connectorName}:${hit.idOnSite}`"
                                    class="text-left bg-elevated rounded-lg p-2 hover:bg-accented"
                                    @click="attachSite(hit)">
                                    <p class="font-medium">{{ hit.name }}</p>
                                    <p class="text-muted text-xs line-clamp-2">{{ hit.description }}</p>
                                </button>
                            </div>
                        </template>
                    </UModal>
                </UCard>
                <MangaMetadataFetcherTable :manga-id="mangaId" />
            </div>
        </div>
        <template #actions>
            <UButton
                :icon="manga?.monitored ? 'i-lucide-bookmark-check' : 'i-lucide-bookmark'"
                :color="manga?.monitored ? 'primary' : 'neutral'"
                variant="soft"
                :loading="togglingMonitor"
                @click="setMonitored(!manga?.monitored)">
                {{ manga?.monitored ? 'Monitored' : 'Unmonitored' }}
            </UButton>
            <UButton icon="i-lucide-pencil" variant="soft" color="secondary" @click="openRename">Rename</UButton>
            <UButton
                icon="i-lucide-history"
                :to="`/actions?mangaId=${mangaId}&return=${$route.fullPath}`"
                variant="soft"
                color="secondary" />
            <UButton variant="soft" color="warning" icon="i-lucide-trash" @click="remove" />
            <UTooltip text="Reload" :kbds="['meta', 'R']">
                <UButton variant="soft" color="secondary" icon="i-lucide-refresh-ccw" :loading="refreshingData" @click="refreshData" />
            </UTooltip>
        </template>
        <UModal v-model:open="renameOpen" title="Rename">
            <template #body>
                <UFormField label="Series name">
                    <UInput v-model="renameName" class="w-full" />
                </UFormField>
                <UCheckbox v-model="renameFolder" class="mt-3" label="Also rename the folder on disk" />
                <p v-if="renameError" class="text-error text-sm mt-2">{{ renameError }}</p>
            </template>
            <template #footer>
                <div class="flex justify-end gap-2">
                    <UButton variant="ghost" @click="renameOpen = false">Cancel</UButton>
                    <UButton :loading="renaming" :disabled="!renameName.trim()" @click="saveRename">Save</UButton>
                </div>
            </template>
        </UModal>
    </MangaDetailPage>
</template>

<script setup lang="ts">
import MangaDetailPage from '~/components/MangaDetailPage.vue';
const { $api } = useNuxtApp();
const route = useRoute();
const mangaId = route.params.mangaId as string;

const flashDownloading = route.hash.substring(1) == 'download';

const { data: manga } = await useApi('/v2/Manga/{MangaId}', {
    path: { MangaId: mangaId },
    key: FetchKeys.Manga.Id(mangaId),
    onResponseError: (e) => {
        console.error(e);
        navigateTo('/');
    },
    lazy: true,
    server: false,
});

const { data: connectors } = await useApi('/v2/MangaConnector', { key: FetchKeys.MangaConnector.All, server: false });
const availableSites = computed(() =>
    (connectors.value ?? []).filter((c: { name: string; enabled?: boolean }) => c.name !== 'Global' && c.enabled !== false),
);
const linkOf = (name: string) =>
    manga.value?.mangaConnectorIds?.find((id: { mangaConnectorName: string }) => id.mangaConnectorName === name);

type SiteHit = { name: string; description?: string; connectorName: string; idOnSite: string };
const addSiteOpen = ref(false);
const addSiteName = ref('');
const addSiteQuery = ref('');
const addSiteLastQuery = ref('');
const addSiteBusy = ref(false);
const addSiteSearched = ref(false);
const addSiteHits = ref<SiteHit[]>([]);

const openAddSite = (name: string) => {
    addSiteName.value = name;
    addSiteQuery.value = manga.value?.name ?? '';
    addSiteLastQuery.value = '';
    addSiteHits.value = [];
    addSiteSearched.value = false;
    addSiteOpen.value = true;
};

const searchAddSite = async () => {
    const q = addSiteQuery.value.trim();
    if (!q || !addSiteName.value) return;
    addSiteBusy.value = true;
    addSiteSearched.value = true;
    addSiteLastQuery.value = q;
    addSiteHits.value = [];
    try {
        const params = new URLSearchParams({ query: q });
        const hits = await $fetch<SiteHit[]>(
            `/v2/Manga/${encodeURIComponent(mangaId)}/OnMangaConnector/${encodeURIComponent(addSiteName.value)}?${params.toString()}`,
        );
        addSiteHits.value = hits ?? [];
    } catch {
        addSiteHits.value = [];
    } finally {
        addSiteBusy.value = false;
    }
};

const attachSite = async (hit: SiteHit) => {
    await $api('/v2/Manga/{MangaId}/Sources/{MangaConnectorName}', {
        method: 'POST',
        path: { MangaId: mangaId, MangaConnectorName: hit.connectorName || addSiteName.value },
        body: { idOnSite: hit.idOnSite },
    });
    addSiteOpen.value = false;
    await refreshNuxtData(FetchKeys.Manga.Id(mangaId));
};

const setRequestedFrom = async (MangaConnectorName: string, IsRequested: boolean) => {
    await $api('/v2/Manga/{MangaId}/DownloadFrom/{MangaConnectorName}/{IsRequested}', {
        method: 'PATCH',
        path: { MangaId: mangaId, MangaConnectorName: MangaConnectorName, IsRequested: IsRequested },
    });
    await refreshNuxtData(FetchKeys.Manga.Id(mangaId));
};

const remove = async () => {
    await $api('/v2/Manga/{MangaId}', { method: 'DELETE', path: { MangaId: mangaId } });
    await refreshNuxtData(FetchKeys.Manga.All);
    navigateTo('/');
};

const togglingMonitor = ref(false);
const refreshingChapters = ref(false);
const checkItems = [
    { label: 'Daily', value: 'Daily' },
    { label: 'Weekly', value: 'Weekly' },
];

type MonitorSeries = {
    monitored?: boolean;
    newChapterCheck?: string;
    lastNewChapterCheck?: string | null;
    fileLibraryId?: string | null;
};

const series = computed(() => manga.value as MonitorSeries | null | undefined);

const lastCheckLabel = computed(() => {
    const raw = series.value?.lastNewChapterCheck;
    if (!raw) return '';
    const at = new Date(raw);
    if (Number.isNaN(at.getTime()) || at.getFullYear() < 2000) return '';
    return at.toLocaleString();
});

const patchMonitor = async (monitored: boolean, newChapterCheck?: string) => {
    togglingMonitor.value = true;
    try {
        await $fetch(`/v2/Manga/${encodeURIComponent(mangaId)}/Monitor`, {
            method: 'PATCH',
            body: { monitored, newChapterCheck },
        });
        await refreshNuxtData([FetchKeys.Manga.Id(mangaId), FetchKeys.Manga.All, FetchKeys.Chapters.Manga(mangaId)]);
    } finally {
        togglingMonitor.value = false;
    }
};

const setMonitored = async (value: boolean) => {
    await patchMonitor(value, series.value?.newChapterCheck);
};

const asInterval = (value: unknown): 'Daily' | 'Weekly' | undefined => {
    if (value === 'Daily' || value === 'Weekly') return value;
    if (value && typeof value === 'object' && 'value' in value) {
        const inner = (value as { value: unknown }).value;
        if (inner === 'Daily' || inner === 'Weekly') return inner;
    }
    return undefined;
};

const setCheckInterval = async (value: unknown) => {
    const interval = asInterval(value);
    if (!interval) return;
    await patchMonitor(true, interval);
};

const refreshChapters = async () => {
    refreshingChapters.value = true;
    try {
        await $fetch(`/v2/Manga/${encodeURIComponent(mangaId)}/RefreshChapters`, { method: 'POST' });
        await refreshNuxtData([FetchKeys.Manga.Id(mangaId), FetchKeys.Manga.All, FetchKeys.Chapters.Manga(mangaId)]);
    } finally {
        refreshingChapters.value = false;
    }
};

const searchingMissing = ref(false);
const searchMissing = async () => {
    searchingMissing.value = true;
    try {
        const n = await $fetch<number>(`/v2/Manga/${encodeURIComponent(mangaId)}/SearchMissing`, { method: 'POST' });
        await refreshNuxtData(FetchKeys.Chapters.Manga(mangaId));
        if (!n) {
            /* still useful — queue was empty */
        }
    } finally {
        searchingMissing.value = false;
    }
};

const renameOpen = ref(false);
const renameName = ref('');
const renameFolder = ref(false);
const renaming = ref(false);
const renameError = ref('');
const openRename = () => {
    renameName.value = manga.value?.name ?? '';
    renameFolder.value = false;
    renameError.value = '';
    renameOpen.value = true;
};
const saveRename = async () => {
    renaming.value = true;
    renameError.value = '';
    try {
        await $fetch(`/v2/Manga/${encodeURIComponent(mangaId)}`, {
            method: 'PATCH',
            body: { name: renameName.value.trim(), renameFolder: renameFolder.value },
        });
        renameOpen.value = false;
        await refreshNuxtData([FetchKeys.Manga.Id(mangaId), FetchKeys.Manga.All]);
    } catch {
        renameError.value = 'Could not rename.';
    } finally {
        renaming.value = false;
    }
};

const refreshingData = ref(false);
const refreshData = async () => {
    refreshingData.value = true;
    await refreshNuxtData([
        FetchKeys.Manga.Id(mangaId),
        FetchKeys.Metadata.Manga(mangaId),
        FetchKeys.FileLibraries,
        FetchKeys.Chapters.Manga(mangaId),
    ]);
    refreshingData.value = false;
};

defineShortcuts({ meta_r: { usingInput: true, handler: refreshData } });

useHead({ title: 'Manga' });
</script>
