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
                    <h1>Download source priority</h1>
                </template>
                <p class="text-muted text-sm mb-3">
                    For each missing chapter, Mangette uses the first source in this list that has it and is not cooling down.
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
                        Scan library for existing chapters
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
        rescanMessage.value = `Checked ${result?.chaptersChecked ?? 0} chapters, marked ${result?.markedDownloaded ?? 0} as already on disk.`;
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

const setup = reactive({
    listenPort: 8585,
    libraryPath: '',
    libraryName: 'Library',
    tempDownloadPath: '',
    maxConcurrentDownloads: 2,
    downloadLanguage: 'en',
    chapterNamingScheme: '%M - ?V(Vol.%V )Ch.%C?T( - %T)',
});
const savingSetup = ref(false);
const setupMessage = ref('');
const setupOk = ref(false);

const auth = reactive({ enabled: false, username: 'admin', password: '' });
const savingAuth = ref(false);
const authMessage = ref('');
const authOk = ref(false);

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
    const first = fileLibraries.value?.[0];
    setup.libraryPath = first?.basePath ?? value.defaultLibraryPath ?? '';
    setup.libraryName = first?.libraryName ?? 'Library';
    auth.enabled = !!value.authenticationEnabled;
    auth.username = value.authUsername || 'admin';
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

const { data: stats } = useApi('/v2/Stats', { server: false });
const deCamel = (camel: string): string =>
    camel.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/(^\w{1})|(\s+\w{1})/g, (letter) => letter.toUpperCase());

useHead({ title: 'Settings' });
</script>
