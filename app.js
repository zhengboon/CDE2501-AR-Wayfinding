// Navigation offset matches CSS --nav-height (80px) minus some visual padding
const NAV_SCROLL_OFFSET = 72;
const COPY_FEEDBACK_MS = 1000;

// Smooth scrolling
const anchors = document.querySelectorAll('a[href^="#"]');
anchors.forEach(anchor => {
    anchor.addEventListener('click', function handleAnchorClick(event) {
        event.preventDefault();
        const target = document.querySelector(this.getAttribute('href'));
        if (target) {
            window.scrollTo({ top: target.offsetTop - NAV_SCROLL_OFFSET, behavior: 'smooth' });
        }
    });
});

// Mobile navigation
const hamburger = document.getElementById('hamburger');
const navLinks = document.getElementById('navLinks');
if (hamburger && navLinks) {
    hamburger.addEventListener('click', () => {
        const isOpen = navLinks.classList.toggle('open');
        hamburger.classList.toggle('open');
        hamburger.setAttribute('aria-expanded', isOpen);
    });

    navLinks.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            hamburger.classList.remove('open');
            navLinks.classList.remove('open');
            hamburger.setAttribute('aria-expanded', 'false');
        });
    });

    // Close mobile menu on Escape key
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && navLinks.classList.contains('open')) {
            hamburger.classList.remove('open');
            navLinks.classList.remove('open');
            hamburger.setAttribute('aria-expanded', 'false');
            hamburger.focus();
        }
    });
}

// Scroll reveal
const observer = new IntersectionObserver((entries, obs) => {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            obs.unobserve(entry.target);
        }
    });
}, { threshold: 0.12 });

document.querySelectorAll('.show-on-scroll').forEach(node => observer.observe(node));

// Navbar section highlighting — includes the hero header
const navTargets = document.querySelectorAll('section[id], header[id]');
const links = document.querySelectorAll('.nav-links a');

window.addEventListener('scroll', () => {
    const scrollY = window.pageYOffset;
    let current = '';
    navTargets.forEach(section => {
        if (scrollY >= section.offsetTop - 120) {
            current = section.id;
        }
    });

    links.forEach(link => {
        link.classList.toggle('active', link.getAttribute('href') === `#${current}`);
    });
}, { passive: true });

// Copy command buttons
document.querySelectorAll('.btn-copy').forEach(button => {
    button.addEventListener('click', async () => {
        const targetId = button.getAttribute('data-copy-target');
        const source = targetId ? document.getElementById(targetId) : null;
        if (!source) {
            return;
        }

        const text = source.textContent || '';
        try {
            await navigator.clipboard.writeText(text);
            const previous = button.textContent;
            button.textContent = 'Copied';
            setTimeout(() => {
                button.textContent = previous;
            }, COPY_FEEDBACK_MS);
        } catch (_) {
            // Clipboard API may be unavailable on file:// contexts.
            button.textContent = 'Copy failed';
            setTimeout(() => {
                button.textContent = 'Copy';
            }, COPY_FEEDBACK_MS);
        }
    });
});

// Dynamic generated-at timestamp
const generatedAt = document.getElementById('generatedAt');
if (generatedAt) {
    generatedAt.textContent = new Date().toISOString().split('T')[0];
}

// Hero waveform animation — pauses when off-screen for performance
//
// Wave A: multi-frequency sine — 6 full cycles across the viewport width,
//         amplitude-modulated by a slow cosine to create a breathing effect.
// Wave B: exponential-rise sawtooth (repeats every normalised unit) scaled
//         by a slow cosine, producing a rhythmic pulse-train look.
(function runWaveAnimation() {
    const waveA = document.getElementById('wave-a');
    const waveB = document.getElementById('wave-b');
    if (!waveA || !waveB) {
        return;
    }

    // Respect reduced-motion preference
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        return;
    }

    const width = 960;
    const midpoint = 60;
    const amplitudeA = 28;
    const amplitudeB = 22;
    let time = 0;
    let animationId = null;

    const buildPoints = fn => {
        const points = [];
        for (let x = 0; x <= width; x += 4) {
            points.push(`${x},${fn(x)}`);
        }
        return points.join(' ');
    };

    const waveOne = x => midpoint - amplitudeA * Math.sin((x / width) * Math.PI * 6 + time) * Math.cos(time * 0.35 + x * 0.003);
    const waveTwo = x => midpoint - amplitudeB * (1 - Math.exp(-((x / width + time * 0.05) % 1) * 5)) * Math.cos(time * 0.48);

    const tick = () => {
        time += 0.018;
        waveA.setAttribute('points', buildPoints(waveOne));
        waveB.setAttribute('points', buildPoints(waveTwo));
        animationId = requestAnimationFrame(tick);
    };

    // Only animate when the hero section is visible
    const heroSection = document.getElementById('hero');
    if (!heroSection) {
        return;
    }

    const waveObserver = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                if (!animationId) {
                    animationId = requestAnimationFrame(tick);
                }
            } else {
                if (animationId) {
                    cancelAnimationFrame(animationId);
                    animationId = null;
                }
            }
        });
    }, { threshold: 0 });

    waveObserver.observe(heroSection);
})();
