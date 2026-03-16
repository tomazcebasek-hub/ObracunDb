# Splošen opis
V OBRACUN_OSNUTEK_NALOG_OBRACUN se zapisane dejanske šifre artiklov za storitve, že razbite po èasovnih obdobjih


# Opis tabel v bazi (Firebird)

## Poslovne tabele (FA_*)

### FA_DN_NALOG
Delovni nalogi. Vsak nalog ima številko, leto, partnerja, datum, ure in opis (NAZIV1..NAZIV20).
- **STEVILKA** (PK, string) — številka naloga
- **LETO** (PK) — leto naloga
- **PARTNER** — šifra partnerja (? PARTNER.SIFRA)
- **DATUM**, **ZACETEK_URA**, **KONEC_URA**
- **SIF26** — tip storitve (1=servisna, 2=strokovna, 3=programerska)
- **SIF27** — pregledan (0/1)
- **SIF28** — NOM
- **SIF29** — polovièna kilometrina (0=polna, 1=polovièna)
- **SIF30** — oddaljenost
- **NAZIV1..NAZIV20** — opis naloga (20 vrstic)

### FA_ARTIKEL
Šifrant artiklov.
- **SIFRA** (PK, string), **NAZIV**, **NAZIV2**, **ENOTA**

### FA_KOMERCIALIST
Šifrant komercialistov.
- **SIFRA** (PK, string), **PRIIMEK**, **IME**

### FA_PREDRACUN
Predraèuni.
- **STEVILKA** (PK, string), **LETO** (PK)
- **PARTNER** — šifra kupca (? PARTNER.SIFRA)
- **DATUM**, **ZNESEK_KONCNI**

### FA_PREDRACUN_KNJIZBA
Knjižbe (postavke) predraèunov.
- **STEVILKA**, **LETO** — predraèun (? FA_PREDRACUN)

### FA_RACUN
Izdani raèuni.
- **STEVILKA** (PK, string), **LETO** (PK)

### FA_RACUN_KNJIZBA
Knjižbe (postavke) raèunov.

### FA_RACUN_PLACILO
Plaèila raèunov.

### FA_POGODBE
Pogodbe s partnerji.
- **PARTNER**, **VELJA_DO**

### FA_POGODBE_POZ
Postavke pogodb.

### PARTNER
Šifrant partnerjev.
- **SIFRA** (PK, int), **NAZIV**, **NASLOV**, **POSTA**, **DAVCNA**

---

## Obraèunske tabele (OBRACUN_*)

### OBRACUN_OSNUTEK
Osnutek obraèuna za partnerja v doloèenem mesecu.
- **MESEC** (PK), **LETO** (PK), **PARTNER** (PK)
- **IMA_POGODBO**, **IMA_PREDRACUN**, **IMA_NALOGE**
- **MINUTE_OBRACUNANE** — minute nalogov ki se obraèunajo
- **MINUTE_NEOBRACUNANE** — minute nalogov ki se NE obraèunajo
- **MINUTE_KORISCENE** — minute korišèene (pogodbe, roèno, paketi)
- **PLUS_MINUTE_PARTNER_MINUTE**, **PLUS_MINUTE_PREDRACUN**, **PLUS_MINUTE_ROCNO**, **PLUS_MINUTE_POGODBA**

### OBRACUN_OSNUTEK_NALOG_OBRACUN
Posamezen delovni nalog znotraj osnutka — ali se obraèuna ali ne.
- **MESEC**, **LETO**, **PARTNER**, **STEVILKA_NALOGA** (string)
- **LETO_NALOGA** — leto delovnega naloga
- **OBRACUNAM** — 1=da, 0=ne
- **SIFRA_ARTIKLA**, **SIFRA_KOMERCIALISTA**
- **KOLICINA**, **PRODAJNA_CENA**
- **MINUTE_NALOG** — minute na nalogu
- **MINUTE_ODSTETE_PARTNER_MINUTE**, **MINUTE_ODSTETE_PREDRACUN**, **MINUTE_ODSTETE_ROCNO**, **MINUTE_ODSTETE_POGODBA** — korišèene minute po viru
- **KOLICINA_FAKTURIRANA**

### OBRACUN_OSNUTEK_POS
Postavke osnutka (roène, iz pogodb, iz nalogov).
- **MESEC** (PK), **LETO** (PK), **PARTNER** (PK), **ZS** (PK)
- **ARTIKEL**, **NAZIV**, **KOLICINA**, **CENA**, **RABAT**
- **NALOG_STEVILKA** (string), **NALOG_LETO**
- **TIP_POSTAVKE** — enum: 0=NAPAKA, 1=ROCNI, 2=POGODBA, 3=NALOG

### OBRACUN_DN
Nastavitve obraèuna za posamezen delovni nalog.
- **STEVILKA** (PK, string), **LETO** (PK)
- **KAJ_OBRACUNAM** — enum: 0=Nedefinirano, 1=KmMin, 2=Niè, 3=Km, 4=Min
- **MINUTE_KI_SE_NE_OBRACUNAJO**

### OBRACUN_MINUTE
Minute dodeljene partnerju (npr. iz kupljenih paketov).
- **ID** (PK, auto), **PARTNER** (? PARTNER.SIFRA)
- **DATUM** — datum vnosa
- **MINUT**, **VELJAVNOST_MESECIH**
- **ZACETEK_MESEC**, **ZACETEK_LETO** — zaèetek veljavnosti
- **OPOMBA**

### OBRACUN_PAKET_MINUTE
Paketi minut (globalni, niso vezani na partnerja).
- **ID** (PK, auto), **DATUM**, **ARTIKEL** (? FA_ARTIKEL.SIFRA), **MINUT**

### OBRACUN_PORABA_MINUT
Poraba minut.

### OBRACUN_PARAMETER
Parametri aplikacije (kljuè-vrednost).
- **IME** (PK, string), **VREDNOST** (string)

### OBRACUN_UPORABNIK
Uporabniki aplikacije.
- **UPORABNISKO_IME** (PK), **GESLO_HASH**, **ADMIN**

### OBRACUN_REVIZIJA
Revizijska sled sprememb.
- **ID** (PK, auto), **DATUM**, **UPORABNIK**
- **TABELA**, **POLJE** — katera tabela/polje se je spremenilo
- **STARA_VREDNOST**, **NOVA_VREDNOST**
- **KONTEKST** — dodatni opis (npr. "Nalog 123/2026")
