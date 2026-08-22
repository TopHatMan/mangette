<template>
    <div class="login-page">
        <div class="login-card">
            <img src="/favicon.svg" alt="" class="login-card__logo" />
            <h1>Mangette</h1>
            <p class="text-muted text-sm mb-4">Sign in to use this library from Caddy or another device.</p>
            <form class="flex flex-col gap-3" @submit.prevent="submit">
                <UFormField label="Username">
                    <UInput v-model="username" autocomplete="username" autofocus class="w-full" />
                </UFormField>
                <UFormField label="Password">
                    <UInput v-model="password" type="password" autocomplete="current-password" class="w-full" />
                </UFormField>
                <UButton type="submit" :loading="busy" block>Log in</UButton>
                <p v-if="error" class="text-error text-sm">{{ error }}</p>
            </form>
        </div>
    </div>
</template>

<script setup lang="ts">
const username = ref('admin');
const password = ref('');
const busy = ref(false);
const error = ref('');

const submit = async () => {
    busy.value = true;
    error.value = '';
    try {
        await $fetch('/v2/Auth/Login', {
            method: 'POST',
            body: { username: username.value, password: password.value },
        });
        await navigateTo('/');
    } catch {
        error.value = 'Wrong username or password.';
    } finally {
        busy.value = false;
    }
};

useHead({ title: 'Login' });
</script>

<style scoped>
.login-page {
    min-height: 100dvh;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #1d1f27;
    padding: 1.5rem;
}
.login-card {
    width: 100%;
    max-width: 22rem;
    background: #25272e;
    border: 1px solid rgba(255, 255, 255, 0.06);
    border-radius: 10px;
    padding: 1.5rem 1.35rem 1.35rem;
}
.login-card h1 {
    font-size: 1.25rem;
    font-weight: 700;
    letter-spacing: -0.03em;
    margin: 0.4rem 0 0.15rem;
}
.login-card__logo {
    width: 2rem;
    height: 2rem;
}
</style>
