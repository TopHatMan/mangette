<template>
    <MangaDetailPage :manga="manga">
        <div class="grid gap-3 max-xl:grid-flow-row-dense min-2xl:grid-cols-[70%_auto] min-xl:grid-cols-[60%_auto] relative min-xl:h-full">
            <ChaptersList :manga-id="mangaId" class="min-xl:h-full min-xl:overflow-y-scroll" />
            <div class="flex flex-col gap-2">
                <UCard :class="[flashDownloading ? 'animate-[flash_0.75s_ease_0.5s]' : '']">
                    <template #header>
                        <h1 class="font-semibold">Download</h1>
                    </template>
                    <p class="text-muted text-xs mb-3">
                        Sites set to Use are monitored. Search missing queues holes for this series only.
                    </p>
                    <UButton
                        class="mb-3 w-full"
                        icon="i-lucide-search"
                        size="sm"
                        :loading="searchingMissing"
                        :disabled="!manga?.fileLibraryId"
                        @click="searchMissing">
                        Search missing
                    </UButton>
                    <LibrarySelect
                        :manga-id="mangaId"
                        :library-id="manga?.fileLibraryId"
                        class="w-full"
                        @library-changed="refreshNuxtData(FetchKeys.Manga.Id(mangaId))" />
                    <div v-if="manga" class="flex flex-col gap-2 mt-3">
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
