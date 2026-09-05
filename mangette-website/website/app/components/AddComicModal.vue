<template>
    <UModal v-bind="$props" title="Add Comic">
        <template #body>
            <div class="flex flex-col gap-2">
                <p class="text-muted text-sm">
                    There is no scraping site for comics, so watched issues come from an expected issue-number range instead
                    of a chapter list. ComicVine metadata is filled in on the first library scan/import.
                </p>
                <UFormField label="Library" required>
                    <USelect
                        v-model="fileLibraryId"
                        :items="comicLibraries?.map((l) => ({ label: `${l.libraryName} (${l.basePath})`, value: l.key })) ?? []"
                        placeholder="Pick a Comic-kind library"
                        class="w-full"
                        :disabled="busy" />
                </UFormField>
                <UFormField label="Series title" required>
                    <UInput v-model="name" placeholder="Amazing Spider-Man" class="w-full" :disabled="busy" />
                </UFormField>
                <UFormField label="Cover image URL" hint="Optional">
                    <UInput v-model="coverUrl" class="w-full" :disabled="busy" />
                </UFormField>
                <div class="grid grid-cols-2 gap-2">
                    <UFormField label="First issue #" required>
                        <UInput v-model.number="issueStart" type="number" min="1" class="w-full" :disabled="busy" />
                    </UFormField>
                    <UFormField label="Last issue #" hint="Blank = ongoing">
                        <UInput v-model.number="issueEnd" type="number" min="1" class="w-full" :disabled="busy" />
                    </UFormField>
                </div>
                <UFormField label="Year" hint="Optional">
                    <UInput v-model.number="year" type="number" class="w-full" :disabled="busy" />
                </UFormField>
                <p v-if="message" class="text-sm" :class="ok ? 'text-success' : 'text-error'">{{ message }}</p>
                <UButton icon="i-lucide-plus" :loading="busy" :disabled="!fileLibraryId || !name" class="w-fit" @click="onAddClick">
                    Add
                </UButton>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();

const { data: fileLibraries } = useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });
const comicLibraries = computed(() => fileLibraries.value?.filter((l) => l.kind === 'Comic') ?? []);

const fileLibraryId = ref<string>();
const name = ref('');
const coverUrl = ref('');
const issueStart = ref(1);
const issueEnd = ref<number>();
const year = ref<number>();
const busy = ref(false);
const message = ref('');
const ok = ref(false);

const onAddClick = async () => {
    if (!fileLibraryId.value || !name.value) return;
    busy.value = true;
    message.value = '';
    try {
        await $api('/v2/Comic', {
            method: 'PUT',
            body: {
                name: name.value,
                coverUrl: coverUrl.value || undefined,
                fileLibraryId: fileLibraryId.value,
                issueStart: Number(issueStart.value),
                issueEnd: issueEnd.value ? Number(issueEnd.value) : undefined,
                year: year.value ? Number(year.value) : undefined,
            },
        });
        ok.value = true;
        message.value = `Added "${name.value}".`;
        name.value = '';
        coverUrl.value = '';
        issueStart.value = 1;
        issueEnd.value = undefined;
        year.value = undefined;
    } catch (e: unknown) {
        ok.value = false;
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        message.value = body || 'Could not add comic.';
    } finally {
        busy.value = false;
    }
};
</script>
