<template>
    <div class="arr-page">
        <div class="arr-page__head">
            <div>
                <h1 class="arr-page__title">Wanted</h1>
                <p class="arr-page__hint">
                    {{ pending ? 'Loading…' : `${data?.totalMissing ?? 0} missing chapters on monitored series` }}
                </p>
            </div>
            <div class="flex flex-wrap gap-2 items-center">
                <UButton icon="i-lucide-folder-search" :loading="scanning" @click="scanDisk">Scan disk for holes</UButton>
                <UButton icon="i-lucide-refresh-ccw" variant="outline" :loading="pending" @click="reload">Refresh</UButton>
            </div>
        </div>

        <p v-if="scanMessage" class="text-sm mb-3" :class="scanOk ? 'text-success' : 'text-muted'">{{ scanMessage }}</p>

        <p class="text-muted text-sm mb-4 max-w-3xl">
            Like Sonarr Missing: monitored series whose files are gone, empty, or unreadable (bad disk) show up here and get
            re-downloaded. Turn a site on for a series to monitor it.
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
                    <tr v-for="row in data?.series ?? []" :key="row.mangaId" class="cursor-pointer" @click="navigateTo(`/manga/${row.mangaId}`)">
                        <td>
                            <img :src="`/v2/Manga/${row.mangaId}/Cover/Medium`" alt="" class="w-10 h-14 object-cover rounded" loading="lazy" />
                        </td>
                        <td class="font-medium">{{ row.name }}</td>
                        <td class="tabular-nums">{{ row.missingCount }}</td>
                        <td class="tabular-nums text-muted">{{ row.downloadedCount }}/{{ row.chapterCount }}</td>
                        <td class="text-sm text-muted">
                            {{ holePreview(row) }}
                        </td>
                    </tr>
                </tbody>
            </table>
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

const pending = ref(true);
const scanning = ref(false);
const scanMessage = ref('');
const scanOk = ref(false);
const data = ref<WantedMissing | null>(null);

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
        const bits = [
            `Checked ${result.chaptersChecked} chapters`,
            `${result.missingMonitored} holes`,
        ];
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

const holePreview = (row: WantedSeries) => {
    const parts = (row.chapters ?? []).slice(0, 8).map((c) => (c.volume ? `v${c.volume}c${c.chapterNumber}` : c.chapterNumber));
    const extra = row.missingCount > parts.length ? ` +${row.missingCount - parts.length}` : '';
    return parts.join(', ') + extra;
};

onMounted(reload);
useHead({ title: 'Wanted' });
</script>
