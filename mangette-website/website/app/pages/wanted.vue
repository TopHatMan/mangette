<template>
    <div class="arr-page">
        <div class="arr-page__head">
            <div>
                <h1 class="arr-page__title">Wanted</h1>
                <p class="arr-page__hint">
                    {{ tab === 'missing' ? missingHint : 'Map leftover files to missing chapters' }}
                </p>
            </div>
            <div class="flex flex-wrap gap-2 items-center">
                <UButtonGroup>
                    <UButton :variant="tab === 'missing' ? 'solid' : 'outline'" @click="tab = 'missing'">Missing</UButton>
                    <UButton :variant="tab === 'import' ? 'solid' : 'outline'" @click="tab = 'import'">Manual Import</UButton>
                </UButtonGroup>
            </div>
        </div>

        <div v-if="tab === 'missing'">
            <div class="flex flex-wrap gap-2 items-center mb-3">
                <UButton icon="i-lucide-folder-search" :loading="scanning" @click="scanDisk">Scan disk for holes</UButton>
                <UButton icon="i-lucide-refresh-ccw" variant="outline" :loading="pending" @click="reload">Refresh</UButton>
            </div>
            <p v-if="scanMessage" class="text-sm mb-3" :class="scanOk ? 'text-success' : 'text-muted'">{{ scanMessage }}</p>
            <p class="text-muted text-sm mb-4 max-w-3xl">
                Monitored holes. Downloads walk series A–Z (one chapter each) then wrap, so one new title cannot hog the queue.
            </p>
            <p v-if="!pending && !(data?.series ?? []).length" class="text-muted">No holes. Scan disk if you think files were lost.</p>
            <div v-else class="overflow-x-auto">
                <table class="arr-table">
                    <thead>
                        <tr>
                            <th></th>
                            <th>Series</th>
                            <th>Missing</th>
                            <th>On disk</th>
                            <th>Holes</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr
                            v-for="row in data?.series ?? []"
                            :key="row.mangaId"
                            class="cursor-pointer"
                            @click="navigateTo(`/manga/${row.mangaId}`)">
                            <td>
                                <img
                                    :src="`/v2/Manga/${row.mangaId}/Cover/Medium`"
                                    alt=""
                                    class="w-10 h-14 object-cover rounded"
                                    loading="lazy" />
                            </td>
                            <td class="font-medium">{{ row.name }}</td>
                            <td class="tabular-nums">{{ row.missingCount }}</td>
                            <td class="tabular-nums text-muted">{{ row.downloadedCount }}/{{ row.chapterCount }}</td>
                            <td class="text-sm text-muted">{{ holePreview(row) }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </div>

        <div v-else>
            <p class="text-muted text-sm mb-3 max-w-3xl">
                Like Sonarr Manual Import. Scan a folder of leftover <code>.cbz</code> files, pick the series and chapter, then
                import. Files already in the series folder are linked; others are copied there.
            </p>
            <form class="flex flex-wrap gap-2 mb-3" @submit.prevent="scanImport">
                <UInput v-model="importFolder" class="grow min-w-64" placeholder="Folder to scan" />
                <UButton type="submit" icon="i-lucide-folder-search" :loading="importScanning">Scan folder</UButton>
                <UButton
                    :disabled="!selectedImport.length"
                    :loading="importing"
                    icon="i-lucide-file-input"
                    @click="commitImport">
                    Import selected ({{ selectedImport.length }})
                </UButton>
            </form>
            <UCheckbox v-model="deleteSource" label="Delete source file after copy" class="mb-3" />
            <p v-if="importMessage" class="text-sm mb-3" :class="importOk ? 'text-success' : 'text-error'">{{ importMessage }}</p>
            <p v-if="preview?.truncated" class="text-muted text-sm mb-2">Showing the first 400 archives.</p>
            <p v-if="!importScanning && preview && !preview.files.length" class="text-muted">No leftover archives in that folder.</p>
            <div v-else-if="preview?.files.length" class="overflow-x-auto">
                <table class="arr-table">
                    <thead>
                        <tr>
                            <th></th>
                            <th>File</th>
                            <th>Series</th>
                            <th>Chapter</th>
                            <th>Guess</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="row in importRows" :key="row.path">
                            <td>
                                <UCheckbox v-model="row.selected" :disabled="!row.mangaId || !row.chapterId" />
                            </td>
                            <td class="text-sm">
                                <p class="font-medium">{{ row.fileName }}</p>
                                <p class="text-muted text-xs truncate max-w-md" :title="row.path">{{ row.path }}</p>
                            </td>
                            <td>
                                <USelect
                                    v-model="row.mangaId"
                                    :items="seriesItems"
                                    class="w-56"
                                    @update:model-value="onSeriesChange(row)" />
                            </td>
                            <td>
                                <USelect v-model="row.chapterId" :items="chapterItems(row)" class="w-40" />
                            </td>
                            <td class="text-muted text-xs tabular-nums">{{ row.score ? `${row.score}%` : '' }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
type WantedChapter = { chapterId: string; chapterNumber: string; volume?: number | null; title?: string | null };
type WantedSeries = {
    mangaId: string;
    name: string;
    missingCount: number;
    downloadedCount: number;
    chapterCount: number;
    chapters: WantedChapter[];
};
type WantedMissing = { totalMissing: number; series: WantedSeries[] };
type ImportFile = {
    path: string;
    fileName: string;
    size: number;
    mangaId?: string | null;
    mangaName?: string | null;
    chapterId?: string | null;
    chapterNumber?: string | null;
    volume?: number | null;
    score: number;
};
type ImportRow = ImportFile & { selected: boolean };
type ImportPreview = {
    folder: string;
    files: ImportFile[];
    series: { mangaId: string; name: string }[];
    truncated: boolean;
};

const tab = ref<'missing' | 'import'>('missing');
const pending = ref(true);
const scanning = ref(false);
const scanMessage = ref('');
const scanOk = ref(false);
const data = ref<WantedMissing | null>(null);

const importFolder = ref('');
const importScanning = ref(false);
const importing = ref(false);
const deleteSource = ref(false);
const importMessage = ref('');
const importOk = ref(false);
const preview = ref<ImportPreview | null>(null);
const importRows = ref<ImportRow[]>([]);
const missingBySeries = ref<Record<string, WantedChapter[]>>({});

const missingHint = computed(() =>
    pending.value ? 'Loading…' : `${data.value?.totalMissing ?? 0} missing chapters on monitored series`,
);
const selectedImport = computed(() => importRows.value.filter((r) => r.selected && r.mangaId && r.chapterId));
const seriesItems = computed(() =>
    (preview.value?.series ?? []).map((s) => ({ label: s.name, value: s.mangaId })),
);

const chapterItems = (row: ImportRow) => {
    const list = row.mangaId ? missingBySeries.value[row.mangaId] ?? [] : [];
    return list.map((c) => ({
        label: c.volume ? `Vol.${c.volume} Ch.${c.chapterNumber}` : `Ch.${c.chapterNumber}`,
        value: c.chapterId,
    }));
};

const reload = async () => {
    pending.value = true;
    try {
        data.value = await $fetch<WantedMissing>('/v2/Wanted/Missing');
    } catch {
        data.value = { totalMissing: 0, series: [] };
    } finally {
        pending.value = false;
    }
};

const scanDisk = async () => {
    scanning.value = true;
    scanMessage.value = '';
    try {
        const result = await $fetch<{
            chaptersChecked: number;
            markedDownloaded: number;
            missingMonitored: number;
            corruptMoved: number;
            queuedDownloads: number;
        }>('/v2/Maintenance/RescanDownloadedChapters', { method: 'POST' });
        scanOk.value = true;
        const bits = [`Checked ${result.chaptersChecked} chapters`, `${result.missingMonitored} holes`];
        if (result.corruptMoved) bits.push(`moved ${result.corruptMoved} corrupt files aside`);
        if (result.queuedDownloads) bits.push(`queued ${result.queuedDownloads} downloads`);
        scanMessage.value = bits.join('. ') + '.';
        await reload();
        await refreshNuxtData(FetchKeys.Manga.All);
    } catch {
        scanOk.value = false;
        scanMessage.value = 'Could not scan the library folder.';
    } finally {
        scanning.value = false;
    }
};

const loadLibraryPath = async () => {
    try {
        const libs = await $fetch<{ key: string; basePath: string }[]>('/v2/FileLibrary');
        if (!importFolder.value && libs?.[0]?.basePath) importFolder.value = libs[0].basePath;
    } catch {
        /* ignore */
    }
};

const ensureMissing = async (mangaId: string) => {
    if (!mangaId || missingBySeries.value[mangaId]) return;
    try {
        missingBySeries.value[mangaId] = await $fetch<WantedChapter[]>(`/v2/Wanted/MissingChapters/${encodeURIComponent(mangaId)}`);
    } catch {
        missingBySeries.value[mangaId] = [];
    }
};

const onSeriesChange = async (row: ImportRow) => {
    row.chapterId = undefined;
    row.selected = false;
    if (row.mangaId) await ensureMissing(row.mangaId);
};

const scanImport = async () => {
    importScanning.value = true;
    importMessage.value = '';
    try {
        const params = new URLSearchParams();
        if (importFolder.value.trim()) params.set('folder', importFolder.value.trim());
        preview.value = await $fetch<ImportPreview>(`/v2/Wanted/ManualImport?${params.toString()}`);
        importRows.value = (preview.value.files ?? []).map((f) => ({
            ...f,
            selected: !!(f.mangaId && f.chapterId),
        }));
        const ids = [...new Set(importRows.value.map((r) => r.mangaId).filter(Boolean))] as string[];
        await Promise.all(ids.map((id) => ensureMissing(id)));
        importOk.value = true;
        importMessage.value = `${importRows.value.length} leftover file${importRows.value.length === 1 ? '' : 's'}.`;
    } catch (e: unknown) {
        importOk.value = false;
        importMessage.value = e instanceof Error ? e.message : 'Scan failed.';
        preview.value = null;
        importRows.value = [];
    } finally {
        importScanning.value = false;
    }
};

const commitImport = async () => {
    importing.value = true;
    importMessage.value = '';
    try {
        const result = await $fetch<{ imported: number; errors: string[] }>('/v2/Wanted/ManualImport', {
            method: 'POST',
            body: {
                items: selectedImport.value.map((r) => ({ path: r.path, mangaId: r.mangaId, chapterId: r.chapterId })),
                deleteSource: deleteSource.value,
            },
        });
        importOk.value = result.imported > 0;
        importMessage.value =
            `Imported ${result.imported}.` + (result.errors?.length ? ` ${result.errors.slice(0, 5).join(' ')}` : '');
        await scanImport();
        await reload();
        await refreshNuxtData(FetchKeys.Manga.All);
    } catch {
        importOk.value = false;
        importMessage.value = 'Import failed.';
    } finally {
        importing.value = false;
    }
};

const holePreview = (row: WantedSeries) => {
    const parts = (row.chapters ?? []).slice(0, 8).map((c) => (c.volume ? `v${c.volume}c${c.chapterNumber}` : c.chapterNumber));
    const extra = row.missingCount > parts.length ? ` +${row.missingCount - parts.length}` : '';
    return parts.join(', ') + extra;
};

onMounted(async () => {
    await Promise.all([reload(), loadLibraryPath()]);
});
useHead({ title: 'Wanted' });
</script>
