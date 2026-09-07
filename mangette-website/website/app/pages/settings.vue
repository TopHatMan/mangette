<template>
    <MangettePage>
        <UPageSection title="Settings" :ui="{ container: 'py-2 sm:py-2 lg:py-2 gap-2' }">
            <template #description>
                <div v-if="settingsStatus === 'error'">
                    <p class="text-warning">Unable to connect to api.</p>
                    <p class="">NUXT_PUBLIC_OPEN_FETCH_API_BASE_URL: {{ $config.public.openFetch.api.baseURL }}</p>
                </div>
            </template>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Paths and downloads</h1>
                </template>
                <p class="text-muted text-sm mb-4">
                    Library is where finished <code>.cbz</code> files go. Temp is for in-progress chapter images. New series use the library automatically.
                </p>
                <div class="grid gap-3 sm:grid-cols-2">
                    <UFormField label="Listen port" hint="Restart Mangette after changing. Default 8585.">
                        <UInput v-model.number="setup.listenPort" type="number" min="1" max="65535" class="w-full" />
                    </UFormField>
                    <UFormField label="Max concurrent downloads">
                        <UInput v-model.number="setup.maxConcurrentDownloads" type="number" min="1" max="64" class="w-full" />
                    </UFormField>
                    <UFormField label="Library folder" class="sm:col-span-2" hint="Finished chapters. Created if missing.">
                        <UInput v-model="setup.libraryPath" class="w-full" :placeholder="settings?.defaultLibraryPath || './Manga'" />
                    </UFormField>
                    <UFormField label="Library name">
                        <UInput v-model="setup.libraryName" class="w-full" placeholder="Library" />
                    </UFormField>
                    <UFormField label="Download language">
                        <UInput v-model="setup.downloadLanguage" class="w-full" placeholder="en" />
                    </UFormField>
                    <UFormField label="Temp / incomplete downloads" class="sm:col-span-2" hint="Images land here while a chapter is downloading, then the folder is cleaned up.">
                        <UInput v-model="setup.tempDownloadPath" class="w-full" placeholder="data/incomplete" />
                    </UFormField>
                    <UFormField label="Chapter file name" class="sm:col-span-2" hint="%M title, %V volume, %C chapter, %T chapter title">
                        <UInput v-model="setup.chapterNamingScheme" class="w-full" />
                    </UFormField>
                    <UFormField
                        label="New-chapter check"
                        class="sm:col-span-2"
                        hint="Default for newly added monitored series. Ongoing titles are scanned so the chapter total can grow. Each series can override this.">
                        <USelect v-model="setup.defaultNewChapterCheck" :items="checkItems" class="w-full" />
                    </UFormField>
                </div>
                <UButton class="mt-4 w-fit" :loading="savingSetup" @click="saveSetup">Save paths and downloads</UButton>
                <p v-if="setupMessage" class="mt-2 text-sm" :class="setupOk ? 'text-success' : 'text-error'">{{ setupMessage }}</p>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Libraries</h1>
                </template>
                <template #footer>
                    <div class="flex flex-row gap-2">
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addLibraryModal.open()">Add FileLibrary</UButton>
                        <UTooltip :text="komgaConnected ? 'Disconnect Komga' : 'Connect Komga'">
                            <UButton
                                :icon="komgaConnected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                class="w-fit"
                                label="Komga"
                                @click="onKomgaClick" />
                        </UTooltip>
                        <UTooltip :text="kavitaConnected ? 'Disconnect Kavita' : 'Connect Kavita'">
                            <UButton
                                :icon="kavitaConnected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                class="w-fit"
                                label="Kavita"
                                @click="onKavitaClick" />
                        </UTooltip>
                    </div>
                </template>
                <FileLibraries />
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Cloudflare bypass</h1>
                </template>
                <p class="text-muted text-sm mb-3">
                    Chromium on this Windows/Linux machine is enough for Cloudflare. You do <strong>not</strong> need FlareSolverr
                    or Docker if <em>Test Chromium</em> succeeds. Leave the URL empty. “Connection refused” on 192.168.1.210 means
                    the Debian VM is not listening — that is a VM/network issue, not Mangette.
                </p>
                <div class="flex flex-wrap gap-2 mb-3">
                    <UButton variant="outline" :loading="testingChromium" @click="testChromium">Test Chromium</UButton>
                </div>
                <div class="flex max-sm:flex-col flex-row gap-2 items-stretch">
                    <UInput v-model="flareUrl" class="grow" placeholder="optional, e.g. http://192.168.1.210:8191" />
                    <UButton class="w-fit" :loading="savingFlare" @click="saveFlare">Save</UButton>
                    <UButton class="w-fit" variant="outline" :disabled="!flareUrl" :loading="testingFlare" @click="testFlare">
                        Test FlareSolverr
                    </UButton>
                </div>
                <p v-if="flareMessage" class="mt-2 text-sm" :class="flareOk ? 'text-success' : 'text-error'">{{ flareMessage }}</p>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Login (Caddy / phone)</h1>
                </template>
                <p class="text-muted text-sm mb-3">
                    Optional, like Sonarr Forms auth. Enable this if you reverse-proxy Mangette with Caddy to a phone. LAN-only
                    use can leave it off. Lockout: set <code>authenticationEnabled</code> to false in
                    <code>data/settings.json</code>.
                </p>
                <div class="grid gap-3 sm:grid-cols-2">
                    <UFormField label="Username">
                        <UInput v-model="auth.username" class="w-full" autocomplete="username" />
                    </UFormField>
                    <UFormField label="Password" hint="Leave blank to keep the current password.">
                        <UInput v-model="auth.password" type="password" class="w-full" autocomplete="new-password" />
                    </UFormField>
                </div>
                <UCheckbox v-model="auth.enabled" class="mt-3" label="Require login" />
                <UButton class="mt-4 w-fit" :loading="savingAuth" @click="saveAuth">Save login</UButton>
                <p v-if="authMessage" class="mt-2 text-sm" :class="authOk ? 'text-success' : 'text-error'">{{ authMessage }}</p>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Comic downloads</h1>
                </template>
                <p class="text-muted text-sm mb-3">
                    Comics have no scraping connector. Prowlarr searches your indexers, and the winning release goes to
                    qBittorrent (torrent) or SABnzbd (Usenet) depending on protocol preference. ComicVine is the metadata
                    source used to match series and build the wanted-issue list on import.
                </p>
                <div class="grid gap-3 sm:grid-cols-2">
                    <UFormField label="Prowlarr URL" class="sm:col-span-2">
                        <UInput v-model="comic.prowlarrUrl" class="w-full" placeholder="http://192.168.1.50:9696" />
                    </UFormField>
                    <UFormField label="Prowlarr API key">
                        <UInput v-model="comic.prowlarrApiKey" type="password" class="w-full" />
                    </UFormField>
                    <div class="flex items-end">
                        <UButton variant="outline" :loading="testingProwlarr" @click="testProwlarr">Test Prowlarr</UButton>
                    </div>
                    <div class="sm:col-span-2">
                        <div class="flex items-center gap-2 mb-2">
                            <p class="text-sm font-medium">Indexers</p>
                            <UButton size="xs" variant="outline" :loading="loadingIndexers" @click="loadIndexers">
                                {{ indexers.length ? 'Reload' : 'Load indexers' }}
                            </UButton>
                            <UButton
                                v-if="indexers.length"
                                size="xs"
                                :loading="savingIndexers"
                                class="ml-auto"
                                @click="saveIndexers">
                                Save indexer selection
                            </UButton>
                        </div>
                        <p v-if="indexersMessage" class="text-sm mb-2" :class="indexersOk ? 'text-success' : 'text-error'">
                            {{ indexersMessage }}
                        </p>
                        <p v-if="!indexers.length && !loadingIndexers" class="text-muted text-xs">
                            Load indexers to pick which ones Comics searches. None selected = search every indexer Prowlarr has.
                        </p>
                        <div v-else class="flex flex-col gap-1 max-h-64 overflow-y-auto">
                            <label
                                v-for="ix in indexers"
                                :key="ix.id"
                                class="flex items-center gap-2 bg-elevated rounded-lg px-2 py-1.5 cursor-pointer">
                                <UCheckbox v-model="ix.selectedForComics" />
                                <span class="grow text-sm">{{ ix.name }}</span>
                                <UBadge size="sm" variant="subtle" :color="ix.protocol === 'Usenet' ? 'secondary' : 'primary'">
                                    {{ ix.protocol }}
                                </UBadge>
                                <UBadge v-if="!ix.enabledInProwlarr" size="sm" variant="outline" color="warning">
                                    Disabled in Prowlarr
                                </UBadge>
                            </label>
                        </div>
                    </div>
                    <UFormField
                        label="Search categories"
                        class="sm:col-span-2"
                        hint="Newznab/Torznab category ids to restrict searches to. Leave blank (the default) to search every category, same as a plain Prowlarr search -- indexers are inconsistent about tagging comics as 7030 vs. the generic Books categories, so restricting here can hide real results.">
                        <div class="flex gap-2">
                            <UInput v-model="comicCategoriesText" class="w-full" placeholder="Blank = no restriction, e.g. 7000,7010,7020,7030,7040,7060" />
                            <UButton variant="outline" :loading="savingCategories" @click="saveCategories">Save</UButton>
                        </div>
                    </UFormField>
                    <UFormField
                        label="Test search"
                        class="sm:col-span-2"
                        hint="Runs the exact same Prowlarr search Comics uses, with your current categories/indexer settings above, so you can see directly whether it's reaching Prowlarr and which indexers/releases come back -- no need to dig through server logs.">
                        <div class="flex gap-2 mb-2">
                            <UInput v-model="testSearchQuery" class="w-full" placeholder="e.g. Absolute Superman" @keydown.enter.prevent="testSearch" />
                            <UButton variant="outline" :loading="testingSearch" @click="testSearch">Search</UButton>
                        </div>
                        <p v-if="testSearchResult" class="text-sm mb-1" :class="testSearchOk ? 'text-success' : 'text-error'">
                            {{ testSearchMessage }}
                        </p>
                        <div v-if="testSearchResult?.sampleTitles?.length" class="flex flex-col gap-1 mb-2">
                            <p v-for="(t, i) in testSearchResult.sampleTitles" :key="i" class="text-muted text-xs truncate">{{ t }}</p>
                        </div>
                        <p v-if="testSearchResult" class="text-muted text-xs mb-1">
                            Prowlarr returned {{ testSearchResult.rawResultCount }} raw result(s); {{ testSearchResult.resultCount }} parsed successfully.
                            <span v-if="testSearchResult.httpStatus">HTTP {{ testSearchResult.httpStatus }}.</span>
                            <span v-if="testSearchResult.error" class="text-error"> {{ testSearchResult.error }}</span>
                        </p>
                        <p v-if="testSearchResult?.requestUrl" class="text-muted text-xs">
                            Request sent: <code class="break-all">{{ testSearchResult.requestUrl }}</code>
                            <br />If this shows 0 raw results but Prowlarr's own Search page finds some, paste this URL (with
                            <code>&amp;apikey=...</code> appended) directly into a browser on the same network as Prowlarr to see its raw response.
                        </p>
                    </UFormField>
                    <UFormField label="qBittorrent URL" class="sm:col-span-2">
                        <UInput v-model="comic.qBittorrentUrl" class="w-full" placeholder="http://192.168.1.50:8080" />
                    </UFormField>
                    <UFormField label="qBittorrent username">
                        <UInput v-model="comic.qBittorrentUsername" class="w-full" />
                    </UFormField>
                    <UFormField label="qBittorrent password">
                        <UInput v-model="comic.qBittorrentPassword" type="password" class="w-full" />
                    </UFormField>
                    <UFormField label="qBittorrent category" hint="Set this category's Default Save Path in qBittorrent to route Comics into their own folder.">
                        <UInput v-model="comic.qBittorrentCategory" class="w-full" placeholder="mangette-comic" />
                    </UFormField>
                    <div class="flex items-end">
                        <UButton variant="outline" :loading="testingQBittorrent" @click="testQBittorrent">Test qBittorrent</UButton>
                    </div>
                    <UFormField label="SABnzbd URL" class="sm:col-span-2">
                        <UInput v-model="comic.sabnzbdUrl" class="w-full" placeholder="http://192.168.1.50:8080" />
                    </UFormField>
                    <UFormField label="SABnzbd API key">
                        <UInput v-model="comic.sabnzbdApiKey" type="password" class="w-full" />
                    </UFormField>
                    <UFormField label="SABnzbd category" hint="Set this category's Folder in SABnzbd to route Comics into their own folder.">
                        <UInput v-model="comic.sabnzbdCategory" class="w-full" placeholder="mangette-comic" />
                    </UFormField>
                    <div class="flex items-end">
                        <UButton variant="outline" :loading="testingSabnzbd" @click="testSabnzbd">Test SABnzbd</UButton>
                    </div>
                    <UFormField label="Protocol preference" hint="Which client wins when a search finds both.">
                        <USelect v-model="comic.protocolPreference" :items="['Usenet', 'Torrent']" class="w-full" />
                    </UFormField>
                    <UFormField label="ComicVine API key" class="sm:col-span-2" hint="Free key from comicvine.gamespot.com">
                        <UInput v-model="comic.comicVineApiKey" type="password" class="w-full" />
                    </UFormField>
                    <div class="flex items-end">
                        <UButton variant="outline" :loading="testingComicVine" @click="testComicVine">Test ComicVine</UButton>
                    </div>
                </div>
                <UButton class="mt-4 w-fit" :loading="savingComic" @click="saveComic">Save comic downloads</UButton>
                <p v-if="comicMessage" class="mt-2 text-sm" :class="comicOk ? 'text-success' : 'text-error'">{{ comicMessage }}</p>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Download source priority</h1>
                </template>
                <p class="text-muted text-sm mb-3">
                    Fallback order among the sites you turn on for each series. MangaDex is not used. Pick sites on the series page.
                </p>
                <ol class="flex flex-col gap-2">
                    <li v-for="(name, index) in connectorPriority" :key="name" class="flex items-center gap-2">
                        <span class="w-6 text-muted">{{ index + 1 }}</span>
                        <span class="grow">{{ name }}</span>
                        <UButton size="xs" variant="ghost" icon="i-lucide-chevron-up" :disabled="index === 0" @click="movePriority(index, -1)" />
                        <UButton size="xs" variant="ghost" icon="i-lucide-chevron-down" :disabled="index === connectorPriority.length - 1" @click="movePriority(index, 1)" />
                    </li>
                </ol>
                <UButton class="mt-3 w-fit" :loading="savingPriority" @click="savePriority">Save priority</UButton>
                <p v-if="priorityMessage" class="mt-2 text-sm">{{ priorityMessage }}</p>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Notifications</h1>
                </template>
                <NotificationConnectors />
                <template #footer>
                    <div class="flex flex-row gap-2">
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addGotifyModal.open()">Add Gotify</UButton>
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addNtfyModal.open()">Add Ntfy</UButton>
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addPushoverModal.open()">Add Pushover</UButton>
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addGenericConnectorModal.open()"
                            >Add Generic Notification Connector</UButton
                        >
                    </div>
                </template>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Maintenance</h1>
                </template>
                <div class="flex flex-wrap gap-2">
                    <UButton icon="i-lucide-folder-search" loading-auto class="w-fit mb-2" @click="rescanLibrary">
                        Scan disk for missing and corrupt chapters
                    </UButton>
                    <UButton icon="i-lucide-database" loading-auto class="w-fit mb-2" @click="cleanUpDatabase">
                        Remove leftover search results
                    </UButton>
                    <UButton icon="i-lucide-captions-off" loading-auto class="w-fit mb-2" @click="cleanUpActions">Clean actions</UButton>
                </div>
                <p v-if="rescanMessage" class="text-sm mt-1">{{ rescanMessage }}</p>
            </UCard>
            <UCard>
                <template #header>
                    <h1>Stats</h1>
                </template>
                <div class="flex flex-row flex-wrap gap-2">
                    <UBadge v-for="(value, name) in stats" :key="name" variant="outline" color="neutral">
                        {{ deCamel(name) }}: {{ value }}
                    </UBadge>
                </div>
            </UCard>
        </UPageSection>
    </MangettePage>
</template>

<script setup lang="ts">
import {
    LazyAddLibraryModal,
    LazyGenericNotificationConnectorModal,
    LazyGotifyModal,
    LazyKavitaModal,
    LazyKomgaModal,
    LazyNtfyModal,
    LazyPushoverModal,
} from '#components';
import FileLibraries from '~/components/FileLibraries.vue';
import { refreshNuxtData } from '#app';
const overlay = useOverlay();
const { $api } = useNuxtApp();

const addLibraryModal = overlay.create(LazyAddLibraryModal);
const komgaModal = overlay.create(LazyKomgaModal);
const kavitaModal = overlay.create(LazyKavitaModal);

const addGotifyModal = overlay.create(LazyGotifyModal);
const addNtfyModal = overlay.create(LazyNtfyModal);
const addPushoverModal = overlay.create(LazyPushoverModal);
const addGenericConnectorModal = overlay.create(LazyGenericNotificationConnectorModal);

const cleanUpDatabase = async () => {
    await useApi('/v2/Maintenance/CleanupNoDownloadManga', { method: 'POST' });
    await refreshNuxtData(FetchKeys.Manga.All);
};
const cleanUpActions = async () => {
    await useApi('/v2/Maintenance/CleanupActions', { method: 'POST' });
};
const rescanMessage = ref('');
const rescanLibrary = async () => {
    rescanMessage.value = '';
    try {
        const result = await $api('/v2/Maintenance/RescanDownloadedChapters', { method: 'POST' });
        await refreshNuxtData(FetchKeys.Manga.All);
        const missing = (result as { missingMonitored?: number })?.missingMonitored ?? 0;
        const corrupt = (result as { corruptMoved?: number })?.corruptMoved ?? 0;
        const queued = (result as { queuedDownloads?: number })?.queuedDownloads ?? 0;
        rescanMessage.value =
            `Checked ${result?.chaptersChecked ?? 0} chapters, ${result?.markedDownloaded ?? 0} on disk, ${missing} holes.` +
            (corrupt ? ` Moved ${corrupt} corrupt files aside.` : '') +
            (queued ? ` Queued ${queued} downloads.` : '');
    } catch {
        rescanMessage.value = 'Could not scan the library folder.';
    }
};

const { data: libraries } = useApi('/v2/LibraryConnector', { key: FetchKeys.Libraries.All });
const komgaConnected = computed(() => libraries.value?.find((l) => l.type === 'Komga'));
const onKomgaClick = async () => {
    if (!komgaConnected.value) {
        komgaModal.open();
    } else {
        await $api('/v2/LibraryConnector/{LibraryConnectorId}', {
            method: 'DELETE',
            path: { LibraryConnectorId: komgaConnected.value.key },
        });
        await refreshNuxtData(FetchKeys.Libraries.All);
    }
};
const kavitaConnected = computed(() => libraries.value?.find((l) => l.type === 'Kavita'));
const onKavitaClick = async () => {
    if (!kavitaConnected.value) {
        kavitaModal.open();
    } else {
        await $api('/v2/LibraryConnector/{LibraryConnectorId}', {
            method: 'DELETE',
            path: { LibraryConnectorId: kavitaConnected.value.key },
        });
        await refreshNuxtData(FetchKeys.Libraries.All);
    }
};

const { data: settings, status: settingsStatus } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });
const { data: fileLibraries } = useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });
const flareUrl = ref('');
const savingFlare = ref(false);
const testingFlare = ref(false);
const testingChromium = ref(false);
const flareMessage = ref('');
const flareOk = ref(false);

const connectorPriority = ref<string[]>([]);
const savingPriority = ref(false);
const priorityMessage = ref('');

const checkItems = [
    { label: 'Daily', value: 'Daily' },
    { label: 'Weekly', value: 'Weekly' },
];
const setup = reactive({
    listenPort: 8585,
    libraryPath: '',
    libraryName: 'Library',
    tempDownloadPath: '',
    maxConcurrentDownloads: 2,
    downloadLanguage: 'en',
    chapterNamingScheme: '%M - ?V(Vol.%V )Ch.%C?T( - %T)',
    defaultNewChapterCheck: 'Daily',
});
const savingSetup = ref(false);
const setupMessage = ref('');
const setupOk = ref(false);

const auth = reactive({ enabled: false, username: 'admin', password: '' });
const savingAuth = ref(false);
const authMessage = ref('');
const authOk = ref(false);

const comic = reactive({
    prowlarrUrl: '',
    prowlarrApiKey: '',
    qBittorrentUrl: '',
    qBittorrentUsername: '',
    qBittorrentPassword: '',
    qBittorrentCategory: 'mangette-comic',
    sabnzbdUrl: '',
    sabnzbdApiKey: '',
    sabnzbdCategory: 'mangette-comic',
    comicVineApiKey: '',
    protocolPreference: 'Usenet',
});
const savingComic = ref(false);
const comicMessage = ref('');
const comicOk = ref(false);
const testingProwlarr = ref(false);
const testingQBittorrent = ref(false);
const testingSabnzbd = ref(false);
const testingComicVine = ref(false);

type ProwlarrIndexer = { id: number; name: string; protocol: 'Torrent' | 'Usenet'; enabledInProwlarr: boolean; selectedForComics: boolean };
const indexers = ref<ProwlarrIndexer[]>([]);
const loadingIndexers = ref(false);
const savingIndexers = ref(false);
const indexersMessage = ref('');
const indexersOk = ref(false);

const comicCategoriesText = ref('');
const savingCategories = ref(false);

type ProwlarrTestSearchResult = {
    resultCount: number;
    rawResultCount: number;
    categoriesUsed: number[];
    indexerIdsUsed: number[];
    sampleTitles: string[];
    requestUrl: string;
    httpStatus?: number | null;
    error?: string | null;
};
const testSearchQuery = ref('');
const testingSearch = ref(false);
const testSearchResult = ref<ProwlarrTestSearchResult | null>(null);
const testSearchOk = ref(false);
const testSearchMessage = ref('');

const applySetupFromSettings = () => {
    const value = settings.value;
    if (!value) return;
    flareUrl.value = value.flareSolverrUrl ?? '';
    if (value.connectorPriority?.length) connectorPriority.value = [...value.connectorPriority];
    setup.listenPort = value.listenPort ?? 8585;
    setup.tempDownloadPath = value.tempDownloadPath ?? '';
    setup.maxConcurrentDownloads = value.maxConcurrentDownloads ?? 2;
    setup.downloadLanguage = value.downloadLanguage ?? 'en';
    setup.chapterNamingScheme = value.chapterNamingScheme ?? setup.chapterNamingScheme;
    setup.defaultNewChapterCheck = (value as { defaultNewChapterCheck?: string }).defaultNewChapterCheck ?? 'Daily';
    const first = fileLibraries.value?.find((l) => l.kind === 'Manga') ?? fileLibraries.value?.[0];
    setup.libraryPath = first?.basePath ?? value.defaultLibraryPath ?? '';
    setup.libraryName = first?.libraryName ?? 'Library';
    auth.enabled = !!value.authenticationEnabled;
    auth.username = value.authUsername || 'admin';

    comic.prowlarrUrl = value.prowlarrUrl ?? '';
    comic.prowlarrApiKey = value.prowlarrApiKey ?? '';
    comicCategoriesText.value = (value.comicSearchCategories ?? []).join(',');
    comic.qBittorrentUrl = value.qBittorrentUrl ?? '';
    comic.qBittorrentUsername = value.qBittorrentUsername ?? '';
    comic.qBittorrentPassword = value.qBittorrentPassword ?? '';
    comic.qBittorrentCategory = value.qBittorrentCategory ?? 'mangette-comic';
    comic.sabnzbdUrl = value.sabnzbdUrl ?? '';
    comic.sabnzbdApiKey = value.sabnzbdApiKey ?? '';
    comic.sabnzbdCategory = value.sabnzbdCategory ?? 'mangette-comic';
    comic.comicVineApiKey = value.comicVineApiKey ?? '';
    comic.protocolPreference = value.comicProtocolPreference ?? 'Usenet';
};

watch([settings, fileLibraries], applySetupFromSettings, { immediate: true });

const saveSetup = async () => {
    savingSetup.value = true;
    setupMessage.value = '';
    try {
        const previousPort = settings.value?.listenPort;
        const updated = await $api('/v2/Settings', {
            method: 'PATCH',
            body: {
                listenPort: Number(setup.listenPort),
                tempDownloadPath: setup.tempDownloadPath,
                libraryPath: setup.libraryPath,
                libraryName: setup.libraryName,
                maxConcurrentDownloads: Number(setup.maxConcurrentDownloads),
                downloadLanguage: setup.downloadLanguage,
                chapterNamingScheme: setup.chapterNamingScheme,
                defaultNewChapterCheck: setup.defaultNewChapterCheck,
            },
        });
        if (updated?.listenPort) setup.listenPort = updated.listenPort;
        if (updated?.tempDownloadPath) setup.tempDownloadPath = updated.tempDownloadPath;
        await refreshNuxtData(FetchKeys.Settings.All);
        await refreshNuxtData(FetchKeys.FileLibraries);
        setupOk.value = true;
        setupMessage.value =
            updated?.listenPort && previousPort && updated.listenPort !== previousPort
                ? `Saved. Restart Mangette so it listens on port ${updated.listenPort}.`
                : 'Saved. New series will download into this library.';
    } catch {
        setupOk.value = false;
        setupMessage.value = 'Could not save paths or download settings.';
    } finally {
        savingSetup.value = false;
    }
};

const movePriority = (index: number, delta: number) => {
    const next = index + delta;
    if (next < 0 || next >= connectorPriority.value.length)
        return;
    const copy = [...connectorPriority.value];
    const [item] = copy.splice(index, 1);
    copy.splice(next, 0, item);
    connectorPriority.value = copy;
};

const savePriority = async () => {
    savingPriority.value = true;
    priorityMessage.value = '';
    try {
        const updated = await $api('/v2/Settings/ConnectorPriority', { method: 'PATCH', body: connectorPriority.value });
        connectorPriority.value = updated ?? connectorPriority.value;
        await refreshNuxtData(FetchKeys.Settings.All);
        priorityMessage.value = 'Saved. First in the list is tried first for each chapter.';
    } catch {
        priorityMessage.value = 'Could not save source priority.';
    } finally {
        savingPriority.value = false;
    }
};

const saveAuth = async () => {
    savingAuth.value = true;
    authMessage.value = '';
    try {
        await $fetch('/v2/Settings', {
            method: 'PATCH',
            body: {
                authenticationEnabled: auth.enabled,
                authUsername: auth.username,
                authPassword: auth.password || undefined,
            },
        });
        await refreshNuxtData(FetchKeys.Settings.All);
        auth.password = '';
        authOk.value = true;
        authMessage.value = auth.enabled ? 'Login is on. Open this URL from your phone through Caddy and sign in.' : 'Login is off.';
    } catch (e: unknown) {
        authOk.value = false;
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        authMessage.value = body || 'Could not save login.';
    } finally {
        savingAuth.value = false;
    }
};

const saveFlare = async () => {
    savingFlare.value = true;
    flareMessage.value = '';
    try {
        const updated = await $fetch<{ flareSolverrUrl?: string }>('/v2/Settings', {
            method: 'PATCH',
            body: { flareSolverrUrl: flareUrl.value },
        });
        flareUrl.value = updated?.flareSolverrUrl ?? flareUrl.value;
        await refreshNuxtData(FetchKeys.Settings.All);
        flareOk.value = true;
        flareMessage.value = flareUrl.value ? `Saved ${flareUrl.value}` : 'Cleared.';
    } catch (e: unknown) {
        flareOk.value = false;
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        flareMessage.value = body || 'Could not save FlareSolverr URL.';
    } finally {
        savingFlare.value = false;
    }
};

const testFlare = async () => {
    testingFlare.value = true;
    flareMessage.value = '';
    try {
        const msg = await $fetch<string>('/v2/Settings/FlareSolverr/Test', { method: 'POST' });
        flareOk.value = true;
        flareMessage.value = msg || 'FlareSolverr is reachable.';
    } catch (e: unknown) {
        flareOk.value = false;
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        flareMessage.value =
            body ||
            'Cannot reach FlareSolverr. On the Debian VM: docker compose up -d (host port 8191). From Windows: curl http://192.168.1.210:8191 then save that URL here.';
    } finally {
        testingFlare.value = false;
    }
};

const testChromium = async () => {
    testingChromium.value = true;
    flareMessage.value = '';
    try {
        await $api('/v2/Settings/CloudflareBypass/Test', { method: 'POST' });
        flareOk.value = true;
        flareMessage.value = 'Built-in Chromium loaded a page. Docker is not required.';
    } catch {
        flareOk.value = false;
        flareMessage.value = 'Chromium failed. Install Google Chrome or Edge on this machine.';
    } finally {
        testingChromium.value = false;
    }
};

const apiErrorText = (e: unknown): string => {
    const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
    return body;
};

// Test buttons save their own section first -- otherwise "Test" checks whatever was last
// persisted, not what's currently typed in the box, which reads as "it says empty" even
// though the field visibly has a value in it.
const saveProwlarr = () => $fetch('/v2/Settings/Prowlarr', { method: 'PATCH', body: { url: comic.prowlarrUrl, apiKey: comic.prowlarrApiKey } });
const saveQBittorrent = () =>
    $fetch('/v2/Settings/QBittorrent', {
        method: 'PATCH',
        body: {
            url: comic.qBittorrentUrl,
            username: comic.qBittorrentUsername,
            password: comic.qBittorrentPassword,
            category: comic.qBittorrentCategory,
        },
    });
const saveSabnzbd = () =>
    $fetch('/v2/Settings/Sabnzbd', { method: 'PATCH', body: { url: comic.sabnzbdUrl, apiKey: comic.sabnzbdApiKey, category: comic.sabnzbdCategory } });
const saveComicVineKey = () => $fetch('/v2/Settings/ComicVine', { method: 'PATCH', body: { apiKey: comic.comicVineApiKey } });
const saveProtocolPreference = () => $fetch(`/v2/Settings/ComicProtocolPreference/${comic.protocolPreference}`, { method: 'PATCH' });

const saveComic = async () => {
    savingComic.value = true;
    comicMessage.value = '';
    try {
        await saveProwlarr();
        await saveQBittorrent();
        await saveSabnzbd();
        await saveProtocolPreference();
        await saveComicVineKey();
        await refreshNuxtData(FetchKeys.Settings.All);
        comicOk.value = true;
        comicMessage.value = 'Saved.';
    } catch (e: unknown) {
        comicOk.value = false;
        comicMessage.value = apiErrorText(e) || 'Could not save comic download settings.';
    } finally {
        savingComic.value = false;
    }
};

const testProwlarr = async () => {
    testingProwlarr.value = true;
    comicMessage.value = '';
    try {
        await saveProwlarr();
        const msg = await $fetch<string>('/v2/Settings/Prowlarr/Test', { method: 'POST' });
        comicOk.value = true;
        comicMessage.value = msg || 'Prowlarr is reachable.';
    } catch (e: unknown) {
        comicOk.value = false;
        comicMessage.value = apiErrorText(e) || 'Could not reach Prowlarr.';
    } finally {
        testingProwlarr.value = false;
        await refreshNuxtData(FetchKeys.Settings.All);
    }
};

const loadIndexers = async () => {
    loadingIndexers.value = true;
    indexersMessage.value = '';
    try {
        await saveProwlarr();
        indexers.value = await $fetch<ProwlarrIndexer[]>('/v2/Settings/Prowlarr/Indexers');
        if (!indexers.value.length) indexersMessage.value = 'Prowlarr has no indexers configured.';
    } catch (e: unknown) {
        indexersOk.value = false;
        indexersMessage.value = apiErrorText(e) || 'Could not load Prowlarr indexers.';
    } finally {
        loadingIndexers.value = false;
    }
};

const saveIndexers = async () => {
    savingIndexers.value = true;
    indexersMessage.value = '';
    try {
        const selected = indexers.value.filter((ix) => ix.selectedForComics).map((ix) => ix.id);
        await $fetch('/v2/Settings/Prowlarr/Indexers', { method: 'PATCH', body: selected });
        indexersOk.value = true;
        indexersMessage.value = selected.length
            ? `Saved. Comics will search ${selected.length} indexer${selected.length === 1 ? '' : 's'}.`
            : 'Saved. Comics will search every indexer Prowlarr has.';
    } catch (e: unknown) {
        indexersOk.value = false;
        indexersMessage.value = apiErrorText(e) || 'Could not save indexer selection.';
    } finally {
        savingIndexers.value = false;
    }
};

const testSearch = async () => {
    const q = testSearchQuery.value.trim();
    if (!q) return;
    testingSearch.value = true;
    testSearchResult.value = null;
    try {
        const result = await $fetch<ProwlarrTestSearchResult>('/v2/Settings/Prowlarr/TestSearch', { query: { query: q } });
        testSearchResult.value = result;
        testSearchOk.value = true;
        const cats = result.categoriesUsed.length ? result.categoriesUsed.join(', ') : 'none (unrestricted)';
        const idx = result.indexerIdsUsed.length ? `${result.indexerIdsUsed.length} selected indexer(s)` : 'every indexer';
        testSearchMessage.value = `Found ${result.resultCount} release(s) searching ${idx}, categories: ${cats}.`;
    } catch (e: unknown) {
        testSearchOk.value = false;
        testSearchMessage.value = apiErrorText(e) || 'Search failed.';
    } finally {
        testingSearch.value = false;
    }
};

const saveCategories = async () => {
    savingCategories.value = true;
    indexersMessage.value = '';
    try {
        const categories = comicCategoriesText.value
            .split(',')
            .map((s) => Number.parseInt(s.trim(), 10))
            .filter((n) => Number.isFinite(n));
        const saved = await $fetch<number[]>('/v2/Settings/Prowlarr/Categories', { method: 'PATCH', body: categories });
        comicCategoriesText.value = (saved ?? []).join(',');
        indexersOk.value = true;
        indexersMessage.value = saved?.length
            ? `Saved. Comics restricts searches to category ${saved.length === 1 ? 'id' : 'ids'} ${saved.join(', ')}.`
            : 'Saved. Comics searches every category (no restriction).';
    } catch (e: unknown) {
        indexersOk.value = false;
        indexersMessage.value = apiErrorText(e) || 'Could not save search categories.';
    } finally {
        savingCategories.value = false;
    }
};

const testQBittorrent = async () => {
    testingQBittorrent.value = true;
    comicMessage.value = '';
    try {
        await saveQBittorrent();
        const msg = await $fetch<string>('/v2/Settings/QBittorrent/Test', { method: 'POST' });
        comicOk.value = true;
        comicMessage.value = msg || 'qBittorrent login works.';
    } catch (e: unknown) {
        comicOk.value = false;
        comicMessage.value = apiErrorText(e) || 'Could not log in to qBittorrent.';
    } finally {
        testingQBittorrent.value = false;
        await refreshNuxtData(FetchKeys.Settings.All);
    }
};

const testSabnzbd = async () => {
    testingSabnzbd.value = true;
    comicMessage.value = '';
    try {
        await saveSabnzbd();
        const msg = await $fetch<string>('/v2/Settings/Sabnzbd/Test', { method: 'POST' });
        comicOk.value = true;
        comicMessage.value = msg || 'SABnzbd is reachable.';
    } catch (e: unknown) {
        comicOk.value = false;
        comicMessage.value = apiErrorText(e) || 'Could not reach SABnzbd.';
    } finally {
        testingSabnzbd.value = false;
        await refreshNuxtData(FetchKeys.Settings.All);
    }
};

const testComicVine = async () => {
    testingComicVine.value = true;
    comicMessage.value = '';
    try {
        await saveComicVineKey();
        const msg = await $fetch<string>('/v2/Settings/ComicVine/Test', { method: 'POST' });
        comicOk.value = true;
        comicMessage.value = msg || 'ComicVine API key works.';
    } catch (e: unknown) {
        comicOk.value = false;
        comicMessage.value = apiErrorText(e) || 'Could not verify the ComicVine API key.';
    } finally {
        testingComicVine.value = false;
        await refreshNuxtData(FetchKeys.Settings.All);
    }
};

const { data: stats } = useApi('/v2/Stats', { server: false });
const deCamel = (camel: string): string =>
    camel.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/(^\w{1})|(\s+\w{1})/g, (letter) => letter.toUpperCase());

useHead({ title: 'Settings' });
</script>
