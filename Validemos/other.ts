// Definiciones de interfaces y clases necesarias

interface OneDataTv {
    getChannel(): OneDataChannel | null;
}

interface ChannelInfo {
    getId(): string;
}

interface OneDataChannel {
    getId(): string;
}

class InvalidEpgException extends Error {
    constructor(message: string) {
        super(message);
        this.name = "InvalidEpgException";
    }
}

interface EpgValidator {
    validate(tv: OneDataTv, channelInfo: ChannelInfo): void;
}

class ChannelInfoEpgValidator implements EpgValidator {
    public validate(tv: OneDataTv, channelInfo: ChannelInfo): void {
        const channel = tv.getChannel() || null;
        if (!channel) {
            throw new InvalidEpgException("No channel data found.");
        }
        if (channelInfo.getId() !== channel.getId()) {
            throw new InvalidEpgException(`Invalid channel id ${channelInfo.getId()} (expected ${channel.getId()}).`);
        }
    }
}

class RequiredFieldsEpgValidator implements EpgValidator {
    public validate(tv: OneDataTv, channelInfo: ChannelInfo): void {
        // Implement validation logic
    }
}

class ScheduleOverlapsEpgValidator implements EpgValidator {
    public validate(tv: OneDataTv, channelInfo: ChannelInfo): void {
        // Implement validation logic
    }
}

const validators: EpgValidator[] = [
    new ChannelInfoEpgValidator(),
    new RequiredFieldsEpgValidator(),
    new ScheduleOverlapsEpgValidator(),
];

function applyAllValidators(tv: OneDataTv, channelInfo: ChannelInfo): void {
    validators.forEach(validator => validator.validate(tv, channelInfo));
}

function checkEPGScheduleForOverlaps(programs: AgileTvEPGProgramme[]): void {
    const sortedPrograms = programs.sort((a, b) => a.getStart().localeCompare(b.getStart()));

    for (let i = 0; i < sortedPrograms.length; i++) {
        if (i === sortedPrograms.length - 1) {
            break;
        }
        const currentProgram = sortedPrograms[i];
        const nextProgram = sortedPrograms[i + 1];

        if (currentProgram.getStop() !== nextProgram.getStart()) {
            const formatter = 'YYYY-MM-DDTHH:mm:ssZ';
            const currentProgramStopTime = new Date(currentProgram.getStop());
            const nextProgramStartTime = new Date(nextProgram.getStart());

            if (currentProgramStopTime > nextProgramStartTime) {
                throw new Error(`Conflicting times with emission ${currentProgram.getId()} with stop time [${currentProgram.getStop()}] and next emission ${nextProgram.getId()} with start time [${nextProgram.getStart()}]`);
            }
        }
    }
}

// Definición mínima necesaria para AgileTvEPGProgramme

interface AgileTvEPGProgramme {
    getStart(): string;
    getStop(): string;
    getId(): string;
}
