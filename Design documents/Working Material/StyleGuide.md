Here you go, Andy — a **clean, production‑ready two‑part style guide** that you can apply directly to your Avalonia app:

- **Part 1: Fluent‑2 (Desktop‑first)**
- **Part 2: Material‑Mobile Variant (Touch‑first)**

This gives you a unified design language that feels modern, professional, and consistent across Windows, macOS, Linux, Android, and iOS.

I’ve structured this exactly the way real design systems are written: **tokens → components → layout → motion → accessibility → dark/night mode.**

# ⭐ PART 1 — Fluent‑2 Style Guide (Desktop‑First)

## 🎨 1. Design Tokens

### **Colour Palette**

- **Primary:** `#0F6CBD` (Fluent Blue)
- **Secondary:** `#005FB8`
- **Surface:** `#FFFFFF` (light), `#1C1C1C` (dark)
- **Accent:** `#C83D95` (magenta)
- **Neutral:** greys in 8–12% increments (`#F3F3F3`, `#E5E5E5`, `#D1D1D1`, …)

### **Elevation**

Fluent 2 is *flat*. Use **0–2 subtle shadows** only for:

- Flyouts
- Command bars
- Dialogs

### **Corner Radius**

- Buttons: **4px**
- Panels: **6px**
- Dialogs: **8px**

### **Typography**

- **Display:** Segoe UI Variable
- **Body:** Segoe UI
- **Weights:** Regular, Semibold
- **Tracking:** Slightly expanded for readability

## 🧱 2. Components

### **Buttons**

- Flat, high‑contrast
- Minimal shadow
- Hover: subtle background tint
- Pressed: darker tint
- Disabled: reduced opacity, no border

### **Panels / Cards**

- No heavy elevation
- 1px border using neutral grey
- 8–12px padding
- Title bar optional

### **Tabs**

- Underline indicator
- No raised surfaces
- 12px horizontal padding

### **Sliders / Numeric Inputs**

- Crisp, rectangular tracks
- Thumb radius 6px
- Clear focus ring (2px accent colour)

### **Data Density**

Fluent is **desktop‑dense**:

- 4–8px vertical rhythm
- 12–16px horizontal rhythm
- Controls can sit closer together than Material

## 📐 3. Layout

### **Grid**

- 12‑column desktop grid
- 8px spacing unit
- Panels grouped by functional domain (Movement, Status, Guiding, Advanced)

### **Responsive Behaviour**

- Desktop: multi‑column
- Tablet: two columns
- Mobile: collapse into Material variant (see Part 2)

### **Navigation**

- Left docked navigation or top command bar
- Avoid hamburger menus on desktop

## 🎞 4. Motion

- Fast, subtle transitions (120–180ms)
- Fade + slight scale for dialogs
- No elastic or spring animations
- Focus on clarity, not personality

## ♿ 5. Accessibility

- Minimum contrast: **WCAG AA**
- Focus ring always visible
- Keyboard navigation first‑class
- Hit targets: 32px minimum (desktop)

## 🌙 6. Dark & Night Mode

### **Dark Mode**

- Surfaces: `#1C1C1C`
- Borders: `#2A2A2A`
- Text: `#EDEDED`
- Accent colours unchanged

### **Night Mode (Astronomy‑specific)**

- Background: **#000000**
- Text: **#FF3B3B** (red)
- No blue light
- No shadows
- No animations
- Reduced brightness on accents

# ⭐ PART 2 — Material‑Mobile Variant (Touch‑First)

This is the mobile personality of your app. It keeps the same colours and typography but shifts to Material spacing, elevation, and interaction patterns.

## 🎨 1. Design Tokens

### **Colour Palette**

Use Fluent colours but apply Material rules:

- Primary: `#0F6CBD`
- Secondary: `#005FB8`
- Surface: `#FFFFFF` / `#121212`
- Error: `#B3261E`

### **Elevation**

Material uses **layered elevation**:

- Cards: 1–3dp
- Bottom sheets: 8dp
- FAB: 6dp
- App bar: 4dp

### **Corner Radius**

- Buttons: **8px**
- Cards: **12px**
- Sheets: **16px**

### **Typography**

- Roboto or Segoe UI Variable
- Larger sizes for touch ergonomics
- 1.2–1.4 line height

## 🧱 2. Components

### **Buttons**

- Filled primary
- Filled tonal secondary
- Elevated optional
- Large hit targets (48px)

### **Cards**

- Rounded
- Soft elevation
- 16px padding
- Clear separation between sections

### **Navigation**

- Bottom navigation bar
- Optional FAB for primary action
- Hamburger menu acceptable on mobile

### **Sliders / Inputs**

- Thick tracks
- Large thumbs
- Clear labels above controls
- Touch‑friendly spacing

### **Dialogs / Sheets**

- Use **bottom sheets** for mount actions
- Use **modal dialogs** for confirmations
- Use **full‑screen dialogs** for complex settings

## 📐 3. Layout

### **Grid**

- 4‑column mobile grid
- 16px spacing unit
- 24px outer margin

### **Responsive Behaviour**

- Collapse desktop panels into **stacked cards**
- Movement controls → single card
- Status → top card
- Guiding → collapsible section
- Advanced → bottom sheet

## 🎞 4. Motion

Material uses expressive motion:

- 200–250ms transitions
- Slide + fade for sheets
- Scale + fade for dialogs
- Easing: standard Material cubic curves

## ♿ 5. Accessibility

- Hit targets: **48px minimum**
- High‑contrast mode supported
- Reduced motion mode respected
- Larger text mode supported

## 🌙 6. Dark & Night Mode

### **Dark Mode**

- Surfaces: `#121212`
- Elevation via shadow colour, not brightness
- Text: `#EDEDED`

### **Night Mode**

Same rules as Fluent night mode:

- Red‑only UI
- No elevation
- No animations
- No blue light
- No translucency